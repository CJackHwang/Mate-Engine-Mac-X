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
        // CGWindowBounds already use the top-left-of-main Y-down convention.
        e.y = (int)bounds.origin.y;
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
    CGEventRef event = CGEventCreate(NULL);
    if (event) {
        CGPoint loc = CGEventGetLocation(event);
        CFRelease(event);
        *x = (float)loc.x;
        *y = (float)loc.y;
        return;
    }
    NSPoint ns = [NSEvent mouseLocation];
    CGFloat mainH = CGDisplayBounds(CGMainDisplayID()).size.height;
    *x = (float)ns.x;
    *y = (float)(mainH - ns.y);
}

// Brings the app's key/visible window in front of the current window level.
// orderFrontRegardless works even when the app is inactive, which is what a
// desk-pet window needs when the user switches to another application.
void MacWin_BringSelfToFront(void)
{
    NSWindow *window = [NSApp keyWindow];
    if (!window) {
        NSArray<NSWindow *> *windows = [NSApp windows];
        for (NSWindow *w in windows) {
            if (w.isVisible) {
                window = w;
                break;
            }
        }
    }
    if (!window) return;
    [window orderFrontRegardless];
}

// Returns the frontmost normal (layer 0) window with its top-left Y-down bounds.
int MacWin_GetFrontNormalWindow(int* x, int* y, int* w, int* h,
                                int* pid, int* windowNumber)
{
    if (!x || !y || !w || !h || !pid || !windowNumber) return 0;
    *x = *y = *w = *h = *pid = *windowNumber = 0;

    CFArrayRef list = CGWindowListCopyWindowInfo(
        kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements,
        kCGNullWindowID);
    if (!list) return 0;

    int found = 0;
    for (CFIndex i = 0; i < CFArrayGetCount(list); i++) {
        CFDictionaryRef info = (CFDictionaryRef)CFArrayGetValueAtIndex(list, i);
        if (!info) continue;

        CFNumberRef layerRef = (CFNumberRef)CFDictionaryGetValue(info, kCGWindowLayer);
        int layer = 0;
        if (layerRef) CFNumberGetValue(layerRef, kCFNumberIntType, &layer);
        if (layer != 0) continue;

        CFNumberRef alphaRef = (CFNumberRef)CFDictionaryGetValue(info, kCGWindowAlpha);
        if (alphaRef) {
            float alpha = 1.0f;
            CFNumberGetValue(alphaRef, kCFNumberFloatType, &alpha);
            if (alpha <= 0.01f) continue;
        }

        CFDictionaryRef boundsRef = (CFDictionaryRef)CFDictionaryGetValue(info, kCGWindowBounds);
        if (!boundsRef) continue;
        CGRect bounds;
        if (!CGRectMakeWithDictionaryRepresentation(boundsRef, &bounds)) continue;
        if (bounds.size.width <= 0.0 || bounds.size.height <= 0.0) continue;

        CFNumberRef pidRef = (CFNumberRef)CFDictionaryGetValue(info, kCGWindowOwnerPID);
        int ownerPid = 0;
        if (pidRef) CFNumberGetValue(pidRef, kCFNumberIntType, &ownerPid);

        CFNumberRef numRef = (CFNumberRef)CFDictionaryGetValue(info, kCGWindowNumber);
        int number = 0;
        if (numRef) CFNumberGetValue(numRef, kCFNumberIntType, &number);

        *x = (int)bounds.origin.x;
        *y = (int)bounds.origin.y;
        *w = (int)bounds.size.width;
        *h = (int)bounds.size.height;
        *pid = ownerPid;
        *windowNumber = number;
        found = 1;
        break;
    }

    CFRelease(list);
    return found;
}
