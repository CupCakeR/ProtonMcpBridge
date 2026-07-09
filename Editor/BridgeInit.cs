using System.Collections.Generic;
using Editor;
using Sandbox;

namespace ProtonMcpBridge;

/// <summary>
/// Starts and stops the listener on the editor hotload lifecycle. [EditorEvent.Hotload] fires on
/// initial load and after every recompile, so it drives (re)start; Start is idempotent.
///
/// Across a hotload of this addon the listener socket would be orphaned and keep the port bound. A
/// live instance held in a static field receives IHotloadManaged.Destroyed before the assembly
/// swaps, stopping the old listener while it's still reachable - so the port is free before the new
/// assembly rebinds.
/// </summary>
internal sealed class BridgeInit : IHotloadManaged
{
	// Held statically so the hotloader visits it and calls Destroyed() before the swap.
	static BridgeInit instance;

	[EditorEvent.Hotload]
	static void OnHotload()
	{
		instance ??= new BridgeInit();
		TcpMcpServer.Start();
	}

	void IHotloadManaged.Destroyed(Dictionary<string, object> state)
	{
		// Assembly is about to be replaced - free the port so the incoming version can rebind.
		TcpMcpServer.Stop();
	}
}
