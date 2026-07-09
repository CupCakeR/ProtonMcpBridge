using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ProtonMcpBridge;

/// <summary>
/// The JSON-RPC 2.0 / MCP shell: handles initialize and ping, routes tools/list and tools/call to
/// <see cref="EngineBridge"/>.
/// </summary>
internal static class Protocol
{
	/// <summary>
	/// Protocol revisions this speaks, newest first. An unknown request is answered with the newest.
	/// </summary>
	static readonly string[] SupportedProtocolVersions =
	[
		"2025-11-25",
		"2025-06-18",
		"2025-03-26",
		"2024-11-05",
	];

	/// <summary>
	/// Sent at initialize and injected into the agent's context - registry-wide conventions that apply
	/// across every tool.
	/// </summary>
	static readonly string Instructions = """
		This server is embedded in the s&box editor and operates on the project that is currently open in it. Start with editor_status to see what's open and whether play mode is running. Your tool list is just the entry points - the real tools live in editor and addon code and come and go as code hotloads. Find them with search_tools, invoke them with call_tool, or batch several invocations with call_tools. After doing anything, read_console shows what the editor logged - errors and exceptions land there. Tool failures come back as results with isError rather than protocol errors, so read them and adapt.

		Conventions, everywhere in the registry:
		- Paging is 'limit' and 'offset'. Defaults and ranges are part of each parameter's schema, and out of range values clamp rather than error.
		- Vectors and angles are comma strings, not arrays or objects - a position is 'x,y,z', view angles are 'pitch,yaw,roll'.
		- The coordinate system is Source engine convention, like Half-Life 2: one unit is one inch, +x is forward, +y is left, +z is up. Angles are degrees.
		- Game objects and components are identified by guid. Assets are identified by the relative path asset_search returns.
		- Every tool that edits the scene pushes an undo step, so the user can ctrl+z your changes like their own.
		""";

	/// <summary>
	/// Handle a single message, returning null when it doesn't warrant a response (notifications).
	/// Runs on the thread pool - tool calls marshal onto the main thread inside the registry.
	/// </summary>
	public static async Task<JsonRpcResponse> Handle(JsonRpcMessage message)
	{
		// Notifications get no response; a missing method means a response to a server-initiated request.
		if (message.IsNotification || message.Method is null)
			return null;

		try
		{
			var result = message.Method switch
			{
				"initialize" => Initialize(message.Params),
				"ping" => new JsonObject(),
				"tools/list" => EngineBridge.ListJson(),
				"tools/call" => await ToolsCall(message.Params),
				_ => throw new McpException(
					JsonRpcError.MethodNotFound,
					$"Method not found: {message.Method}"
				),
			};

			return JsonRpcResponse.Success(message.Id, result);
		}
		catch (McpException e)
		{
			return JsonRpcResponse.Failure(message.Id, e.Code, e.Message);
		}
		catch (Exception e)
		{
			return JsonRpcResponse.Failure(message.Id, JsonRpcError.InternalError, e.Message);
		}
	}

	static JsonNode Initialize(JsonNode param)
	{
		var requested = param?["protocolVersion"]?.ToString();
		var version = SupportedProtocolVersions.Contains(requested)
			? requested
			: SupportedProtocolVersions[0];

		return new JsonObject
		{
			["protocolVersion"] = version,
			["capabilities"] = new JsonObject
			{
				["tools"] = new JsonObject { ["listChanged"] = false },
			},
			["serverInfo"] = new JsonObject
			{
				["name"] = "sbox-editor",
				["title"] = "s&box Editor (Proton bridge)",
				["version"] = $"{Sandbox.Application.Version}",
				["description"] =
					"The s&box editor - live tools for the project open in it, served over a Wine-safe TCP transport",
			},
			["instructions"] = Instructions,
		};
	}

	static async Task<JsonNode> ToolsCall(JsonNode param)
	{
		var name = param?["name"]?.ToString()?.Trim();

		if (string.IsNullOrEmpty(name))
			throw new McpException(JsonRpcError.InvalidParams, "Missing tool name");

		return await EngineBridge.Call(name, param?["arguments"]);
	}
}
