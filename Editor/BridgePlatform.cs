using System;
using System.Runtime.InteropServices;

namespace ProtonMcpBridge;

/// <summary>
/// Gates the bridge to Linux. Under Proton the editor is a Windows build, so the runtime reports
/// Windows and <see cref="RuntimeInformation.IsOSPlatform"/> can't detect Linux directly; the test
/// is instead whether ntdll exports <c>wine_get_version</c>, which only Wine's does. On genuine
/// Windows the built-in editor MCP server works, so the bridge does nothing.
/// </summary>
internal static class BridgePlatform
{
	// Field initializer, not an auto-property: the code generator can't capture a non-constant
	// property default as a constant attribute value.
	static readonly bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || IsWine();

	/// <summary>True on a native Linux runtime, or a Windows build under Wine/Proton.</summary>
	public static bool IsLinux => isLinux;

	static bool IsWine()
	{
		try
		{
			var ntdll = GetModuleHandle("ntdll.dll");
			return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
		}
		catch (Exception)
		{
			// Native interop unavailable or ntdll not shaped as expected - treat as not-Wine.
			return false;
		}
	}

	[DllImport(
		"kernel32.dll",
		EntryPoint = "GetModuleHandleA",
		CharSet = CharSet.Ansi,
		ExactSpelling = true
	)]
	static extern IntPtr GetModuleHandle(string moduleName);

	[DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Ansi)]
	static extern IntPtr GetProcAddress(IntPtr module, string procName);
}
