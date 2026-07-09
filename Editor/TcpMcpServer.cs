using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.Diagnostics;

namespace ProtonMcpBridge;

/// <summary>
/// Loopback MCP server over <see cref="TcpListener"/>, speaking HTTP/1.1 directly. The kernel-backed
/// <see cref="HttpListener"/> the built-in server uses isn't implemented under Wine/Proton;
/// TcpListener is. Binds 127.0.0.1 only and owns the whole port, so non-/mcp paths return 404.
/// </summary>
internal static class TcpMcpServer
{
	static readonly Logger log = new("ProtonMcpBridge");

	const long MaxRequestSize = 8 * 1024 * 1024;
	static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

	static TcpListener listener;
	static CancellationTokenSource cancel;
	static bool loggedWindowsSkip;

	public static int Port { get; private set; }

	public static bool IsRunning => listener is not null;

	/// <summary>The endpoint the listener reports as bound. Null when not running.</summary>
	public static string BoundEndpoint => listener?.LocalEndpoint?.ToString();

	/// <summary>The url MCP clients connect to, null when not running.</summary>
	public static string Url => IsRunning ? $"http://127.0.0.1:{Port}/mcp" : null;

	public static void Start()
	{
		if (IsRunning)
			return;

		// Linux/Proton only; the built-in server covers native Windows.
		if (!BridgePlatform.IsLinux)
		{
			if (!loggedWindowsSkip)
			{
				log.Info(
					"Not starting - native Windows editor; the built-in MCP server handles this platform."
				);
				loggedWindowsSkip = true;
			}
			return;
		}

		if (!BridgeConfig.Enabled)
			return;

		// Refuse to start if the registry reflection target is missing.
		if (!EngineBridge.Available)
		{
			log.Warning(
				$"Not starting - engine bridge unavailable: {EngineBridge.Diagnose()}. The editor's MCP registry (Editor.Mcp.ToolRegistry) may have moved or been renamed."
			);
			return;
		}

		Port = BridgeConfig.Port;

		try
		{
			listener = new TcpListener(IPAddress.Loopback, Port);

			// Reuse the address so a restart can rebind over a socket still in TIME_WAIT. Set before Start().
			listener.ExclusiveAddressUse = false;

			listener.Start();
		}
		catch (Exception e)
		{
			log.Warning($"Couldn't start Proton MCP bridge on port {Port} ({e.Message})");
			listener = null;
			return;
		}

		cancel = new CancellationTokenSource();
		_ = ListenLoop(listener, cancel.Token);

		log.Info($"Proton MCP bridge listening on {Url}");

		BridgeDock.NotifyChanged();
	}

	public static void Stop()
	{
		if (listener is null)
			return;

		try
		{
			cancel?.Cancel();
			listener.Stop();
		}
		catch (Exception)
		{
			// already stopped
		}

		cancel?.Dispose();
		cancel = null;
		listener = null;

		BridgeDock.NotifyChanged();
	}

	public static void Restart()
	{
		Stop();
		Start();
	}

	static async Task ListenLoop(TcpListener incoming, CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			TcpClient client;

			try
			{
				client = await incoming.AcceptTcpClientAsync(token);
			}
			catch (Exception)
			{
				// listener was stopped
				return;
			}

			_ = Task.Run(() => HandleClient(client, token));
		}
	}

	static async Task HandleClient(TcpClient client, CancellationToken token)
	{
		using (client)
		using (var stream = client.GetStream())
		using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
		{
			timeout.CancelAfter(RequestTimeout);

			try
			{
				await HandleRequest(stream, timeout.Token);
			}
			catch (HttpTooLargeException)
			{
				await TryWriteStatus(stream, HttpStatusCode.RequestEntityTooLarge);
			}
			catch (OperationCanceledException)
			{
				// timed out or shutting down
			}
			catch (Exception e)
			{
				log.Warning(e, $"Unhandled exception handling MCP request: {e.Message}");
				await TryWriteStatus(stream, HttpStatusCode.InternalServerError);
			}
		}
	}

	static async Task HandleRequest(NetworkStream stream, CancellationToken token)
	{
		var request = await HttpRequest.Read(stream, MaxRequestSize, token);

		if (request is null)
			return; // clean close or unparseable request

		// A browser sends Origin on cross-site requests, and a malicious page can resolve its own
		// domain to 127.0.0.1 (dns rebinding), so only loopback origins are allowed.
		if (request.Header("Origin") is string origin && !IsLoopbackOrigin(origin))
		{
			await WriteStatus(stream, HttpStatusCode.Forbidden, token);
			return;
		}

		if (request.NormalizedPath != "/mcp")
		{
			await WriteStatus(stream, HttpStatusCode.NotFound, token);
			return;
		}

		if (request.Method != "POST")
		{
			// No server-initiated streams (GET) or sessions (DELETE)
			await WriteStatus(
				stream,
				HttpStatusCode.MethodNotAllowed,
				token,
				extraHeaders: "Allow: POST\r\n"
			);
			return;
		}

		await HandlePost(request, stream, token);
	}

	static async Task HandlePost(HttpRequest request, NetworkStream stream, CancellationToken token)
	{
		JsonRpcMessage message;

		try
		{
			message = JsonSerializer.Deserialize<JsonRpcMessage>(request.Body);
		}
		catch (JsonException)
		{
			// Batches (arrays) were removed in protocol revision 2025-06-18
			var error = request.Body.AsSpan().TrimStart().StartsWith("[")
				? JsonRpcResponse.Failure(
					null,
					JsonRpcError.InvalidRequest,
					"Batching is not supported"
				)
				: JsonRpcResponse.Failure(null, JsonRpcError.ParseError, "Parse error");

			await WriteJson(stream, HttpStatusCode.BadRequest, error, token);
			return;
		}

		if (message is null)
		{
			await WriteJson(
				stream,
				HttpStatusCode.BadRequest,
				JsonRpcResponse.Failure(null, JsonRpcError.InvalidRequest, "Invalid request"),
				token
			);
			return;
		}

		var result = await Protocol.Handle(message);

		// Notifications are acknowledged without a body
		if (result is null)
		{
			await WriteStatus(stream, HttpStatusCode.Accepted, token);
			return;
		}

		await WriteJson(stream, HttpStatusCode.OK, result, token);
	}

	static async Task WriteJson(
		NetworkStream stream,
		HttpStatusCode status,
		JsonRpcResponse payload,
		CancellationToken token
	)
	{
		var body = JsonSerializer.SerializeToUtf8Bytes(payload);

		var head = Encoding.ASCII.GetBytes(
			$"HTTP/1.1 {(int)status} {ReasonPhrase(status)}\r\n"
				+ "Content-Type: application/json\r\n"
				+ $"Content-Length: {body.Length}\r\n"
				+ "Connection: close\r\n"
				+ "\r\n"
		);

		await stream.WriteAsync(head, token);
		await stream.WriteAsync(body, token);
		await stream.FlushAsync(token);
	}

	static async Task WriteStatus(
		NetworkStream stream,
		HttpStatusCode status,
		CancellationToken token,
		string extraHeaders = null
	)
	{
		var head = Encoding.ASCII.GetBytes(
			$"HTTP/1.1 {(int)status} {ReasonPhrase(status)}\r\n"
				+ (extraHeaders ?? "")
				+ "Content-Length: 0\r\n"
				+ "Connection: close\r\n"
				+ "\r\n"
		);

		await stream.WriteAsync(head, token);
		await stream.FlushAsync(token);
	}

	/// <summary>Best-effort status write from catch blocks, where the token may already be tripped.</summary>
	static async Task TryWriteStatus(NetworkStream stream, HttpStatusCode status)
	{
		try
		{
			await WriteStatus(stream, status, CancellationToken.None);
		}
		catch (Exception)
		{
			// connection gone
		}
	}

	static string ReasonPhrase(HttpStatusCode status) =>
		status switch
		{
			HttpStatusCode.OK => "OK",
			HttpStatusCode.Accepted => "Accepted",
			HttpStatusCode.BadRequest => "Bad Request",
			HttpStatusCode.Forbidden => "Forbidden",
			HttpStatusCode.NotFound => "Not Found",
			HttpStatusCode.MethodNotAllowed => "Method Not Allowed",
			HttpStatusCode.RequestEntityTooLarge => "Payload Too Large",
			HttpStatusCode.InternalServerError => "Internal Server Error",
			_ => status.ToString(),
		};

	static bool IsLoopbackOrigin(string origin)
	{
		return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
	}
}
