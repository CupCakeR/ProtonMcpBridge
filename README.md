# Proton MCP Bridge

An s&box editor addon that runs the editor's MCP server on Linux under Proton.

The built-in MCP server uses `HttpListener`, which relies on the Windows http.sys stack that Wine
doesn't implement, so it never starts under Proton. This addon serves the same tools over a
`TcpListener`-based HTTP transport instead. Only the transport changes; the tool list comes from the
editor's own registry.

Linux only. On native Windows it stays inactive, since the built-in server already works there.

## Install

1. Copy this folder into your project's `Libraries/`, or add it via the [library manager](https://sbox.game/ccr/proton-mcp-bridge).
2. Open the project in the editor. The bridge starts automatically.
3. Check it's running: **View > MCP Bridge**, or look for `Proton MCP bridge listening on
   http://127.0.0.1:7269/mcp` in the console.

## Connect a client

The server speaks MCP over streamable HTTP at:

```
http://127.0.0.1:7269/mcp
```

Point any MCP client at that endpoint from the Linux host (not from inside Proton).

**OpenCode**, add to `opencode.json`:

```json
{
  "mcp": {
    "sbox": {
      "type": "remote",
      "url": "http://127.0.0.1:7269/mcp",
      "enabled": true
    }
  }
}
```

**Claude Code**:

```sh
claude mcp add --transport http sbox http://127.0.0.1:7269/mcp
```

## Config

**View > MCP Bridge** has a status light and a Start/Stop button.

Enabled state and port are shared with the editor's built-in **MCP Server** settings
(Settings > MCP Server), so that tab configures the bridge too. Changes made there apply the next
time the bridge starts (use the dock's Start/Stop or `mcp_bridge_restart`).

Console commands:

```
mcp_bridge_status     show running state and bound endpoint
mcp_bridge_restart    stop and rebind the listener
mcp_bridge_port 7300  change the port (default 7269)
mcp_bridge_selftest   check the listener is reachable from inside the editor
```
