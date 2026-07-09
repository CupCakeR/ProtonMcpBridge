using System;

namespace ProtonMcpBridge;

/// <summary>
/// Bridge settings, shared with the editor's built-in "MCP Server" preferences (Settings > MCP
/// Server) by reading the same EditorCookies, so that tab configures the bridge too. The default
/// port 7269 is the built-in server's, which is free on Linux since that server can't bind it under
/// Proton. Changes from the dock apply immediately; changes from the preferences tab apply on the
/// next start/restart, since the tab doesn't notify us.
/// </summary>
internal static class BridgeConfig
{
	const string EnabledKey = "McpServerEnabled";
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

	/// <summary>Loopback port. Rebinds if already running.</summary>
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
