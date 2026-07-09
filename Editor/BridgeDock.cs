using Editor;

namespace ProtonMcpBridge;

/// <summary>
/// Dockable panel (Window > MCP Bridge): a status light, the connect URL, and a start/stop button.
/// </summary>
[Dock("Editor", "MCP Bridge", "cable")]
internal class BridgeDock : Widget
{
	static readonly Color RunningColor = Color.Parse("#4caf50") ?? Color.Green;

	/// <summary>The live instance, so state changes elsewhere can refresh the panel.</summary>
	internal static BridgeDock Instance { get; private set; }

	readonly Label statusLabel;
	readonly Label urlLabel;
	readonly Button toggleButton;

	public BridgeDock(Widget parent)
		: base(parent)
	{
		Instance = this;

		Layout = Layout.Column();
		Layout.Margin = 16;
		Layout.Spacing = 8;

		statusLabel = new Label("", this);
		Layout.Add(statusLabel);

		urlLabel = new Label("", this) { Color = Theme.TextControl.WithAlpha(0.6f) };
		Layout.Add(urlLabel);

		Layout.AddSpacingCell(4);

		toggleButton = new Button("", this) { Clicked = OnToggle };
		Layout.Add(toggleButton);

		Layout.AddStretchCell();

		Refresh();
	}

	void OnToggle()
	{
		if (!BridgePlatform.IsLinux)
			return;

		// The setter starts or stops the listener.
		BridgeConfig.Enabled = !TcpMcpServer.IsRunning;
		Refresh();
	}

	/// <summary>Push current state into the widgets.</summary>
	public void Refresh()
	{
		if (!BridgePlatform.IsLinux)
		{
			statusLabel.Text = "⚪ Inactive";
			statusLabel.Color = Theme.TextControl.WithAlpha(0.6f);
			urlLabel.Text =
				"Native Windows - the built-in editor MCP server handles this platform.";
			toggleButton.Text = "Unavailable on Windows";
			toggleButton.Enabled = false;
			Update();
			return;
		}

		var running = TcpMcpServer.IsRunning;

		statusLabel.Text = running ? "🟢 Running" : "⚫ Stopped";
		statusLabel.Color = running ? RunningColor : Theme.TextControl.WithAlpha(0.6f);

		urlLabel.Text = running
			? $"MCP endpoint (streamable HTTP):\n{TcpMcpServer.Url}"
			: $"Port {BridgeConfig.Port}";

		toggleButton.Text = running ? "Stop" : "Start";
		toggleButton.Enabled = true;

		Update();
	}

	/// <summary>Refresh the panel if it's open. Called when the listener starts or stops.</summary>
	internal static void NotifyChanged()
	{
		Instance?.Refresh();
	}
}
