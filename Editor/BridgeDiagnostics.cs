using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Diagnostics;

namespace ProtonMcpBridge;

/// <summary>
/// Loopback reachability check (mcp_bridge_selftest): connects to the listener from inside the editor
/// and does one round trip, confirming the transport is reachable under Wine/Proton.
/// </summary>
internal static class BridgeDiagnostics
{
	static readonly Logger log = new("ProtonMcpBridge");

	public static async Task SelfTest()
	{
		if (!TcpMcpServer.IsRunning)
		{
			log.Info("[selftest] server not running - nothing to probe");
			return;
		}

		log.Info($"[selftest] bound endpoint: {TcpMcpServer.BoundEndpoint ?? "(null)"}");

		await Probe(IPAddress.Loopback, "127.0.0.1");
	}

	static async Task Probe(IPAddress address, string label)
	{
		try
		{
			using var client = new TcpClient(address.AddressFamily);

			var connect = client.ConnectAsync(address, TcpMcpServer.Port);

			if (await Task.WhenAny(connect, Task.Delay(3000)) != connect)
			{
				log.Warning($"[selftest] {label}:{TcpMcpServer.Port} - connect timed out (3s)");
				return;
			}

			await connect; // surface any connect exception

			var body = """{"jsonrpc":"2.0","id":"selftest","method":"ping"}""";
			var request =
				"POST /mcp HTTP/1.1\r\n"
				+ $"Host: {label}:{TcpMcpServer.Port}\r\n"
				+ "Content-Type: application/json\r\n"
				+ $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n"
				+ "Connection: close\r\n\r\n"
				+ body;

			using var stream = client.GetStream();
			await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
			await stream.FlushAsync();

			var buffer = new byte[4096];
			int read = await stream.ReadAsync(buffer);
			var responseLine = Encoding.UTF8.GetString(buffer, 0, read).Split("\r\n")[0];

			log.Info(
				$"[selftest] {label}:{TcpMcpServer.Port} - reachable, round trip ok: {responseLine}"
			);
		}
		catch (Exception e)
		{
			log.Warning(
				$"[selftest] {label}:{TcpMcpServer.Port} - failed: {e.GetType().Name}: {e.Message}"
			);
		}
	}
}
