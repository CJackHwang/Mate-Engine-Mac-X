#import <Cocoa/Cocoa.h>
#import <CoreGraphics/CoreGraphics.h>

typedef struct {
    int x, y, w, h;
    int pid;
    int layer;
    int isOnscreen;
    int windowNumber;
} MacWinEntry;

static MacWinEntry* g_windows = NULL;
static int g_count = 0;
static int g_capacity = 0;

static void ensureCapacity(int needed)
{
    if (needed <= g_capacity) return;
    int newCap = needed * 2;
    g_windows = (MacWinEntry*)realloc(g_windows, newCap * sizeof(MacWinEntry));
    g_capacity = newCap;
}

void MacWin_Refresh(int selfPid)
{
    g_count = 0;

    CFArrayRef list = CGWindowListCopyWindowInfo(
        kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements,
        kCGNullWindowID);
    if (!list) return;

    CFIndex total = CFArrayGetCount(list);
    ensureCapacity((int)total);

    for (CFIndex i = 0; i < total; i++)
    {
        CFDictionaryRef info = (CFDictionaryRef)CFArrayGetValueAtIndex(list, i);
        if (!info) continue;

        // PID
        CFNumberRef pidRef = (CFNumberRef)CFDictionaryGetValue(info, kCGWindowOwnerPID);
        int pid = 0;
        if (pidRef) CFNumberGetValue(pidRef, kCFNumberIntType, &pid);
        if (pid == selfPid) continue;

        // Layer
        CFNumberRef layerRef = (CFNumberRef)CFDictionaryGetValue(info, kCGWindowLayer);
        int layer = 0;
        if (layerRef) CFNumberGetValue(layerRef, kCFNumberIntType, &layer);

        // IsOnscreen
        CFBooleanRef onscreenRef = (CFBooleanRef)CFDictionaryGetValue(info, kCGWindowIsOnscreen);
        int isOnscreen = (onscreenRef && CFBooleanGetValue(onscreenRef)) ? 1 : 0;

        // WindowNumber
        CFNumberRef numRef = (CFNumberRef)CFDictionaryGetValue(info, kCGWindowNumber);
        int windowNumber = 0;
        if (numRef) CFNumberGetValue(numRef, kCFNumberIntType, &windowNumber);

        // Bounds
        CFDictionaryRef boundsRef = (CFDictionaryRef)CFDictionaryGetValue(info, kCGWindowBounds);
        if (!boundsRef) continue;
        CGRect bounds;
        if (!CGRectMakeWithDictionaryRepresentation(boundsRef, &bounds)) continue;

        MacWinEntry e;
        e.x = (int)bounds.origin.x;
        CGFloat screenH = CGDisplayBounds(CGMainDisplayID()).size.height;
        // Store as bottom-left origin: y = bottom edge, Top in RECT = y + h = upper edge
        e.y = (int)(screenH - bounds.origin.y - bounds.size.height);
        e.w = (int)bounds.size.width;
        e.h = (int)bounds.size.height;
        e.pid = pid;
        e.layer = layer;
        e.isOnscreen = isOnscreen;
        e.windowNumber = windowNumber;
        g_windows[g_count++] = e;
    }

    CFRelease(list);
}

int MacWin_GetCount()
{
    return g_count;
}

int MacWin_GetWindow(int index, int* x, int* y, int* w, int* h,
                     int* pid, int* layer, int* isOnscreen, int* windowNumber)
{
    if (index < 0 || index >= g_count) return 0;
    MacWinEntry* e = &g_windows[index];
    *x = e->x; *y = e->y; *w = e->w; *h = e->h;
    *pid = e->pid; *layer = e->layer;
    *isOnscreen = e->isOnscreen;
    *windowNumber = e->windowNumber;
    return 1;
}

void MacWin_GetCursorPos(float* x, float* y)
{
    NSPoint loc = [NSEvent mouseLocation];
    CGFloat screenH = CGDisplayBounds(CGMainDisplayID()).size.height;
    *x = (float)loc.x;
    *y = (float)(screenH - loc.y);
}
