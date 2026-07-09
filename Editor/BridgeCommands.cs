using Sandbox;
using Sandbox.Diagnostics;

namespace ProtonMcpBridge;

/// <summary>
/// Console commands for the bridge - the dock covers everyday start/stop; these are for scripting
/// and the loopback self-test: <c>mcp_bridge_status</c>, <c>mcp_bridge_selftest</c>,
/// <c>mcp_bridge_restart</c>, <c>mcp_bridge_port</c>.
/// </summary>
public static class BridgeCommands
{
	static readonly Logger log = new("ProtonMcpBridge");

	[ConCmd(
		"mcp_bridge_status",
		Help = "Show whether the Proton MCP bridge is running and where it's bound"
	)]
	public static void Status()
	{
		if (!TcpMcpServer.IsRunning)
		{
			if (!BridgePlatform.IsLinux)
			{
				log.Info(
					"[mcp bridge] not running - native Windows editor; the built-in MCP server handles this platform."
				);
				return;
			}

			log.Info(
				$"[mcp bridge] not running. enabled: {BridgeConfig.Enabled}, engine bridge available: {EngineBridge.Available} ({EngineBridge.Diagnose()})"
			);
			return;
		}

		log.Info(
			$"[mcp bridge] running - url {TcpMcpServer.Url}, bound endpoint {TcpMcpServer.BoundEndpoint}"
		);
	}

	[ConCmd(
		"mcp_bridge_selftest",
		Help = "Probe the bridge's loopback listener from inside the editor process"
	)]
	public static void SelfTest()
	{
		_ = BridgeDiagnostics.SelfTest();
	}

	[ConCmd("mcp_bridge_restart", Help = "Stop and restart the Proton MCP bridge listener")]
	public static void Restart()
	{
		TcpMcpServer.Restart();
	}

	[ConCmd(
		"mcp_bridge_port",
		Help = "Set the loopback port and rebind, e.g. mcp_bridge_port 7300"
	)]
	public static void SetPort(int port)
	{
		BridgeConfig.Port = port;
		log.Info($"[mcp bridge] port set to {BridgeConfig.Port}");
	}
}
