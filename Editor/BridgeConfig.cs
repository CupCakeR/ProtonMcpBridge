using System;

namespace ProtonMcpBridge;

/// <summary>
/// Bridge settings, persisted per-user via EditorCookie. The port is shared with the editor's
/// built-in "MCP Server" preferences (it reads the same McpServerPort cookie), so its default is the
/// built-in's 7269 - free on Linux, since that server can't bind it under Proton - and setting the
/// port in that tab configures the bridge too (applies on the next start/restart). Enable/disable is
/// the bridge's own, driven by the dock's Start/Stop.
/// </summary>
internal static class BridgeConfig
{
	const string EnabledKey = "ProtonMcpBridge.Enabled";
	const string PortKey = "McpServerPort";

	public const int DefaultPort = 7269;

	/// <summary>Whether the listener should run. Default on.</summary>
	public static bool Enabled
	{
		get => EditorCookie.Get(EnabledKey, true);
		set
		{
			if (Enabled == value)
				return;

			EditorCookie.Set(EnabledKey, value);

			if (value)
				TcpMcpServer.Start();
			else
				TcpMcpServer.Stop();
		}
	}

	/// <summary>Loopback port, shared with the built-in MCP Server tab. Rebinds if already running.</summary>
	public static int Port
	{
		get => EditorCookie.Get(PortKey, DefaultPort);
		set
		{
			value = Math.Clamp(value, 1, 65535);

			if (Port == value)
				return;

			EditorCookie.Set(PortKey, value);

			if (TcpMcpServer.IsRunning)
				TcpMcpServer.Restart();
		}
	}
}
