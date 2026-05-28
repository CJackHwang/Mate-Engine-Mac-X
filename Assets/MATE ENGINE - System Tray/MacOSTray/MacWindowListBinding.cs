#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
using System.Runtime.InteropServices;

public static class MacWindowListBinding
{
    [DllImport("MacWindowList")]
    public static extern void MacWin_Refresh(int selfPid);

    [DllImport("MacWindowList")]
    public static extern int MacWin_GetCount();

    [DllImport("MacWindowList")]
    public static extern int MacWin_GetWindow(
        int index,
        out int x, out int y, out int w, out int h,
        out int pid, out int layer, out int isOnscreen, out int windowNumber);

    [DllImport("MacWindowList")]
    public static extern void MacWin_GetCursorPos(out float x, out float y);
}
#endif
