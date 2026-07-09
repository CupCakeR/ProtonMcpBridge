using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ProtonMcpBridge;

/// <summary>
/// A parsed HTTP/1.1 request - just enough of the protocol for MCP's streamable HTTP transport:
/// request line, headers to the blank line, then exactly Content-Length bytes. No chunked transfer
/// and no keep-alive; one request per connection.
/// </summary>
internal sealed class HttpRequest
{
	public string Method { get; private init; }
	public string Path { get; private init; }
	public string Body { get; private init; }

	readonly Dictionary<string, string> headers;

	HttpRequest(string method, string path, Dictionary<string, string> headers, string body)
	{
		Method = method;
		Path = path;
		this.headers = headers;
		Body = body;
	}

	public string Header(string name) => headers.TryGetValue(name, out var value) ? value : null;

	/// <summary>The path with any trailing slash trimmed, so "/mcp" and "/mcp/" both match.</summary>
	public string NormalizedPath => Path?.TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : "/";

	/// <summary>
	/// Read one request off the stream. Returns null on a clean close or a malformed head, and throws
	/// <see cref="HttpTooLargeException"/> when the request exceeds <paramref name="maxSize"/>. The head
	/// is read a byte at a time to the blank line; the body is read in bulk by Content-Length.
	/// </summary>
	public static async Task<HttpRequest> Read(
		Stream stream,
		long maxSize,
		System.Threading.CancellationToken cancel = default
	)
	{
		var head = new MemoryStream();
		var one = new byte[1];

		// Read until the CRLFCRLF that ends the headers.
		while (!EndsWithBlankLine(head))
		{
			int read = await stream.ReadAsync(one, cancel);

			if (read == 0)
				return null; // connection closed with no complete request

			head.WriteByte(one[0]);

			if (head.Length > 64 * 1024)
				throw new HttpTooLargeException(); // reject oversized header blocks
		}

		var lines = Encoding
			.ASCII.GetString(head.GetBuffer(), 0, (int)head.Length)
			.Split("\r\n", StringSplitOptions.None);

		var requestLine = lines[0].Split(' ');

		if (requestLine.Length < 2)
			return null;

		var method = requestLine[0];
		var path = requestLine[1];

		var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		for (int i = 1; i < lines.Length; i++)
		{
			if (string.IsNullOrEmpty(lines[i]))
				break;

			var colon = lines[i].IndexOf(':');

			if (colon <= 0)
				continue;

			headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
		}

		var body = "";

		if (
			headers.TryGetValue("Content-Length", out var lengthText)
			&& long.TryParse(lengthText, out var length)
			&& length > 0
		)
		{
			if (length > maxSize)
				throw new HttpTooLargeException();

			body = await ReadBody(stream, (int)length, cancel);
		}

		return new HttpRequest(method, path, headers, body);
	}

	static async Task<string> ReadBody(
		Stream stream,
		int length,
		System.Threading.CancellationToken cancel
	)
	{
		var buffer = new byte[length];
		int offset = 0;

		while (offset < length)
		{
			int read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancel);

			if (read == 0)
				break; // client hung up mid-body

			offset += read;
		}

		return Encoding.UTF8.GetString(buffer, 0, offset);
	}

	static bool EndsWithBlankLine(MemoryStream buffer)
	{
		if (buffer.Length < 4)
			return false;

		var raw = buffer.GetBuffer();
		var end = (int)buffer.Length;

		return raw[end - 4] == '\r'
			&& raw[end - 3] == '\n'
			&& raw[end - 2] == '\r'
			&& raw[end - 1] == '\n';
	}
}

/// <summary>
/// A request that exceeded the size cap - an oversized header block or a Content-Length past the
/// limit - so the caller can answer 413 instead of buffering unbounded input.
/// </summary>
internal sealed class HttpTooLargeException : Exception { }
