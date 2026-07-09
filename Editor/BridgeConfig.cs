using System;

namespace ProtonMcpBridge;

/// <summary>
/// Bridge settings, persisted per-user via EditorCookie. Enabled is the master switch; setting
/// either value applies immediately.
/// </summary>
internal static class BridgeConfig
{
	const string EnabledKey = "ProtonMcpBridge.Enabled";
	const string PortKey = "ProtonMcpBridge.Port";

	public const int DefaultPort = 7270;

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

	/// <summary>Loopback port, not 7269 (the built-in server's). Rebinds if already running.</summary>
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
