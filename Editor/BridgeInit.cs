using System.Collections.Generic;
using Editor;
using Sandbox;

namespace ProtonMcpBridge;

/// <summary>
/// Owns the listener's lifecycle across the editor session.
///
/// Auto-start runs off the tool frame, not a load event: a library loaded after the editor core can
/// miss "editor.created" and the initial "hotloaded", but the frame loop always ticks once we're
/// live, so the first frame after any (re)load starts the server. It's one-shot, so a manual Stop
/// sticks, and Start honours the enabled/platform checks itself.
///
/// Teardown uses IHotloadManaged.Destroyed: the listener socket lives in this assembly, so before a
/// hotload swaps it out we stop the old listener while it's still reachable, freeing the port for
/// the incoming version. A live instance is held in a static field so the hotloader can find it.
/// </summary>
internal sealed class BridgeInit : IHotloadManaged
{
	static BridgeInit instance;
	static bool autoStarted;

	[EditorEvent.Frame]
	static void OnFrame()
	{
		instance ??= new BridgeInit();

		if (autoStarted)
			return;

		autoStarted = true;
		TcpMcpServer.Start();
	}

	void IHotloadManaged.Destroyed(Dictionary<string, object> state)
	{
		TcpMcpServer.Stop();
	}
}
