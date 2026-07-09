using System;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sandbox.Diagnostics;

namespace ProtonMcpBridge;

/// <summary>
/// Reflection seam into the editor's MCP registry (Editor.Mcp.ToolRegistry in Sandbox.Tools), which
/// handles tool discovery, argument binding, main-thread marshalling and result shaping. The type is
/// internal, so reflection is the only access path; every lookup is null-guarded so a rename fails
/// loudly instead of silently.
/// </summary>
internal static class EngineBridge
{
	static readonly Logger log = new("ProtonMcpBridge");

	static readonly Type registry = Type.GetType("Editor.Mcp.ToolRegistry, Sandbox.Tools");

	// Public methods on an internal type; bind broadly to tolerate accessibility changes.
	const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

	static readonly MethodInfo callMethod = registry?.GetMethod(
		"Call",
		Flags,
		[typeof(string), typeof(JsonObject)]
	);
	static readonly MethodInfo listMethod = registry?.GetMethod("ListJson", Flags, Type.EmptyTypes);
	static readonly MethodInfo asArgumentsMethod = registry?.GetMethod(
		"AsArguments",
		Flags,
		[typeof(JsonNode), typeof(string)]
	);

	/// <summary>True when the reflection targets resolved. False means the registry moved or was renamed.</summary>
	public static bool Available =>
		registry is not null && callMethod is not null && listMethod is not null;

	/// <summary>What's missing, for the startup log.</summary>
	public static string Diagnose()
	{
		if (registry is null)
			return "couldn't find type 'Editor.Mcp.ToolRegistry, Sandbox.Tools'";
		if (listMethod is null)
			return "couldn't find ToolRegistry.ListJson()";
		if (callMethod is null)
			return "couldn't find ToolRegistry.Call(string, JsonObject)";
		return "ok";
	}

	/// <summary>The tools/list payload from ToolRegistry.ListJson - only [McpListed] tools appear.</summary>
	public static JsonNode ListJson()
	{
		return (JsonNode)listMethod.Invoke(null, null);
	}

	/// <summary>
	/// Invoke a tool by name. ToolRegistry.Call marshals onto the main thread, binds arguments, runs
	/// the tool and shapes the result (including in-band isError results).
	/// </summary>
	public static async Task<JsonNode> Call(string name, JsonNode arguments)
	{
		var args = AsArguments(arguments);

		try
		{
			var task = (Task<JsonNode>)callMethod.Invoke(null, [name, args]);
			return await task;
		}
		catch (TargetInvocationException e) when (e.InnerException is not null)
		{
			// Unwrap so the real failure surfaces instead of the reflection wrapper.
			throw e.InnerException;
		}
	}

	/// <summary>
	/// Coerce the tool-call 'arguments' node to the JsonObject ToolRegistry.Call expects, reusing the
	/// engine's AsArguments (which tolerates JSON encoded inside a string) when available.
	/// </summary>
	static JsonObject AsArguments(JsonNode node)
	{
		if (node is null)
			return null;

		if (asArgumentsMethod is not null)
		{
			try
			{
				return (JsonObject)asArgumentsMethod.Invoke(null, [node, "arguments"]);
			}
			catch (TargetInvocationException e) when (e.InnerException is not null)
			{
				throw e.InnerException;
			}
		}

		return node as JsonObject
			?? throw new McpException(
				JsonRpcError.InvalidParams,
				"arguments must be a json object like {\"key\": value}"
			);
	}
}
