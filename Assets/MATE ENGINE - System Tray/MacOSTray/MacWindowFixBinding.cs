#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
using System.Runtime.InteropServices;

public static class MacWindowFixBinding
{
    [DllImport("MacWindowFix")]
    private static extern void MacWindowFix_Install();

    public static void Install() => MacWindowFix_Install();
}
#endif
