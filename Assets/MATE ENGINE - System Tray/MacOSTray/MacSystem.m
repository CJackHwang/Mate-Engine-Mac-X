#import <Cocoa/Cocoa.h>
#import <CoreGraphics/CoreGraphics.h>
#import <CoreVideo/CoreVideo.h>
#import <dispatch/dispatch.h>
#import <Foundation/Foundation.h>
#import <IOSurface/IOSurface.h>
#import <malloc/malloc.h>

#if __has_include(<ServiceManagement/SMAppService.h>)
#import <ServiceManagement/SMAppService.h>
#endif
#if __has_include(<ScreenCaptureKit/ScreenCaptureKit.h>)
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#endif

typedef void (*MacMenuActionCallback)(int actionId);
typedef void (*MacMenuRebuildCallback)(void);

int MacSys_IsScreenCaptureAuthorized(void);

static MacMenuActionCallback gMenuActionCallback = NULL;
static MacMenuRebuildCallback gMenuRebuildCallback = NULL;
static NSStatusItem *gStatusItem = nil;
static NSMenu *gStatusMenu = nil;
static id gMenuTarget = nil;
static id gMenuDelegate = nil;
static BOOL gRebuildingMenu = NO;

static id gGlobalEventMonitor = nil;
static id gLocalEventMonitor = nil;
static double gLastInputTime = 0.0;
static BOOL gAnyInputSincePoll = NO;

// Incremented on every NSWorkspace active-space change (three-finger Space
// swipe, Mission Control, full-screen transitions). Polled from Unity so the
// snap-follow can pause while the desktop is animating.
static volatile long gSpaceChangeTick = 0;
static id gSpaceChangeObserver = nil;

static void MacSys_InitSpaceMonitor(void)
{
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        gSpaceChangeObserver = [[[NSWorkspace sharedWorkspace] notificationCenter]
            addObserverForName:NSWorkspaceActiveSpaceDidChangeNotification
                        object:nil
                         queue:nil
                    usingBlock:^(NSNotification *note) {
                        __sync_fetch_and_add(&gSpaceChangeTick, 1);
                    }];
    });
}

@interface MacSysMenuTarget : NSObject
@end

@implementation MacSysMenuTarget
- (void)macSysMenuClicked:(id)sender
{
    if (gMenuActionCallback) {
        gMenuActionCallback((int)[sender tag]);
    }
}
@end

@interface MacSysMenuDelegate : NSObject <NSMenuDelegate>
@end

@implementation MacSysMenuDelegate
- (void)menuNeedsUpdate:(NSMenu *)menu
{
    if (!gRebuildingMenu && gMenuRebuildCallback) {
        gMenuRebuildCallback();
    }
}
@end

static CGFloat MacSys_MainDisplayHeight(void)
{
    return CGDisplayBounds(CGMainDisplayID()).size.height;
}

static CGPoint MacSys_CurrentMouseLocation(void)
{
    CGEventRef event = CGEventCreate(NULL);
    if (event) {
        CGPoint location = CGEventGetLocation(event);
        CFRelease(event);
        return location;
    }
    NSPoint ns = [NSEvent mouseLocation];
    return CGPointMake(ns.x, MacSys_MainDisplayHeight() - ns.y);
}

static NSString *MacSys_LaunchAgentPath(void)
{
    NSString *dir = [NSHomeDirectory() stringByAppendingPathComponent:@"Library/LaunchAgents"];
    return [dir stringByAppendingPathComponent:@"com.Shinymoon.MateEngineX.plist"];
}

static void MacSys_SetLaunchAgentEnabled(BOOL enable)
{
    NSString *path = MacSys_LaunchAgentPath();
    NSFileManager *fm = [NSFileManager defaultManager];

    if (!enable) {
        [fm removeItemAtPath:path error:nil];
        return;
    }

    NSString *dir = [path stringByDeletingLastPathComponent];
    [fm createDirectoryAtPath:dir withIntermediateDirectories:YES attributes:nil error:nil];

    NSString *exe = [[NSBundle mainBundle] executablePath] ?: @"";
    NSDictionary *plist = @{
        @"Label" : @"com.Shinymoon.MateEngineX",
        @"ProgramArguments" : @[ exe ],
        @"RunAtLoad" : @YES,
    };
    [plist writeToFile:path atomically:YES];
}

#pragma mark - Status bar / tray menu

void MacSys_SetMenuCallbacks(MacMenuActionCallback action, MacMenuRebuildCallback rebuild)
{
    gMenuActionCallback = action;
    gMenuRebuildCallback = rebuild;
}

void MacSys_CreateStatusItem(const char *tooltip)
{
    if (gStatusItem) return;
    gStatusItem = [[NSStatusBar systemStatusBar] statusItemWithLength:NSVariableStatusItemLength];
    gStatusItem.button.title = tooltip && tooltip[0] ? [NSString stringWithUTF8String:tooltip] : @"MateEngine";
    gStatusItem.button.toolTip = tooltip && tooltip[0] ? [NSString stringWithUTF8String:tooltip] : @"MateEngine";
    gStatusMenu = [[NSMenu alloc] init];
    gMenuTarget = [[MacSysMenuTarget alloc] init];
    gMenuDelegate = [[MacSysMenuDelegate alloc] init];
    gStatusMenu.delegate = gMenuDelegate;
    gStatusItem.menu = gStatusMenu;
}

void MacSys_SetStatusItemIcon(const uint8_t *png, int pngLen)
{
    if (!gStatusItem || !png || pngLen <= 0) return;
    NSData *data = [NSData dataWithBytes:png length:(NSUInteger)pngLen];
    NSImage *image = [[NSImage alloc] initWithData:data];
    if (!image) return;
    gStatusItem.button.image = image;
    gStatusItem.button.imagePosition = NSImageOnly;
}

void MacSys_RemoveStatusItem(void)
{
    if (gStatusItem) {
        [[NSStatusBar systemStatusBar] removeStatusItem:gStatusItem];
        gStatusItem = nil;
        gStatusMenu = nil;
        gMenuTarget = nil;
        gMenuDelegate = nil;
    }
}

void MacSys_ResetMenu(void)
{
    if (!gStatusMenu) return;
    gRebuildingMenu = YES;
    [gStatusMenu removeAllItems];
    gRebuildingMenu = NO;
}

void MacSys_AddMenuItem(const char *title, int actionId)
{
    if (!gStatusMenu || !title) return;
    NSMenuItem *item = [[NSMenuItem alloc] initWithTitle:[NSString stringWithUTF8String:title]
                                                 action:@selector(macSysMenuClicked:)
                                          keyEquivalent:@""];
    item.target = gMenuTarget;
    item.tag = actionId;
    [gStatusMenu addItem:item];
}

void MacSys_AddSeparator(void)
{
    if (!gStatusMenu) return;
    [gStatusMenu addItem:[NSMenuItem separatorItem]];
}

#pragma mark - Dock icon

void MacSys_SetDockIconVisible(int visible)
{
    [NSApp setActivationPolicy:(visible ? NSApplicationActivationPolicyRegular
                                       : NSApplicationActivationPolicyAccessory)];
}

int MacSys_IsDockIconVisible(void)
{
    return [NSApp activationPolicy] == NSApplicationActivationPolicyRegular;
}

#pragma mark - Global input

static void MacSys_HandleInputEvent(NSEvent *event)
{
    (void)event;
    gLastInputTime = [NSDate timeIntervalSinceReferenceDate];
    gAnyInputSincePoll = YES;
}

void MacSys_InstallInputMonitors(void)
{
    if (gGlobalEventMonitor) return;
    gLastInputTime = [NSDate timeIntervalSinceReferenceDate];
    gGlobalEventMonitor = [NSEvent addGlobalMonitorForEventsMatchingMask:NSEventMaskAny
                                                                 handler:^(NSEvent *event) {
        MacSys_HandleInputEvent(event);
    }];
    gLocalEventMonitor = [NSEvent addLocalMonitorForEventsMatchingMask:NSEventMaskAny
                                                               handler:^NSEvent *(NSEvent *event) {
        MacSys_HandleInputEvent(event);
        return event;
    }];
}

void MacSys_UninstallInputMonitors(void)
{
    if (gGlobalEventMonitor) {
        [NSEvent removeMonitor:gGlobalEventMonitor];
        gGlobalEventMonitor = nil;
    }
    if (gLocalEventMonitor) {
        [NSEvent removeMonitor:gLocalEventMonitor];
        gLocalEventMonitor = nil;
    }
}

double MacSys_GetLastInputAge(void)
{
    return [NSDate timeIntervalSinceReferenceDate] - gLastInputTime;
}

int MacSys_IsAnyKeyPressed(void)
{
    return MacSys_GetLastInputAge() < 0.5;
}

int MacSys_ConsumeGlobalInputActivity(void)
{
    BOOL active = gAnyInputSincePoll;
    gAnyInputSincePoll = NO;
    return active;
}

#pragma mark - Screens / cursor

int MacSys_GetScreenCount(void)
{
    return (int)[[NSScreen screens] count];
}

void MacSys_GetScreenRect(int index, int *x, int *y, int *w, int *h)
{
    *x = *y = *w = *h = 0;
    NSArray<NSScreen *> *screens = [NSScreen screens];
    if (index < 0 || index >= (int)screens.count) return;

    NSRect frame = screens[index].frame;
    CGFloat mainH = MacSys_MainDisplayHeight();
    *x = (int)round(frame.origin.x);
    *y = (int)round(mainH - NSMaxY(frame));
    *w = (int)round(frame.size.width);
    *h = (int)round(frame.size.height);
}

void MacSys_GetMainScreenRect(int *x, int *y, int *w, int *h)
{
    *x = *y = *w = *h = 0;
    NSScreen *mainScreen = [NSScreen mainScreen];
    if (!mainScreen) return;

    NSRect frame = mainScreen.frame;
    CGFloat mainH = MacSys_MainDisplayHeight();
    *x = (int)round(frame.origin.x);
    *y = (int)round(mainH - NSMaxY(frame));
    *w = (int)round(frame.size.width);
    *h = (int)round(frame.size.height);
}

void MacSys_GetScreenVisibleRect(int index, int *x, int *y, int *w, int *h)
{
    *x = *y = *w = *h = 0;
    NSArray<NSScreen *> *screens = [NSScreen screens];
    if (index < 0 || index >= (int)screens.count) return;

    NSRect visible = screens[index].visibleFrame;
    CGFloat mainH = MacSys_MainDisplayHeight();
    *x = (int)round(visible.origin.x);
    *y = (int)round(mainH - NSMaxY(visible));
    *w = (int)round(visible.size.width);
    *h = (int)round(visible.size.height);
}

void MacSys_GetVirtualScreenRect(int *x, int *y, int *w, int *h)
{
    int minX = INT_MAX, minY = INT_MAX, maxX = INT_MIN, maxY = INT_MIN;
    NSArray<NSScreen *> *screens = [NSScreen screens];
    for (NSScreen *screen in screens) {
        int sx, sy, sw, sh;
        MacSys_GetScreenRect((int)[screens indexOfObject:screen], &sx, &sy, &sw, &sh);
        minX = MIN(minX, sx);
        minY = MIN(minY, sy);
        maxX = MAX(maxX, sx + sw);
        maxY = MAX(maxY, sy + sh);
    }
    if (maxX == INT_MIN || maxY == INT_MIN) {
        *x = *y = *w = *h = 0;
        return;
    }
    *x = minX;
    *y = minY;
    *w = maxX - minX;
    *h = maxY - minY;
}

void MacSys_GetCursorPos(float *x, float *y)
{
    CGPoint loc = MacSys_CurrentMouseLocation();
    *x = (float)loc.x;
    *y = (float)loc.y;
}

int MacSys_IsAppActive(void)
{
    return [NSApp isActive];
}

long MacSys_GetSpaceChangeTick(void)
{
    MacSys_InitSpaceMonitor();
    return gSpaceChangeTick;
}

float MacSys_GetMainDisplayHeight(void)
{
    return (float)MacSys_MainDisplayHeight();
}

#pragma mark - Occlusion

int MacSys_IsWindowOccludedAtCursor(void)
{
    if (!MacSys_IsScreenCaptureAuthorized())
    {
        // Fallback without screen-recording permission: use NSWindow occlusion state.
        NSWindow *window = [NSApp keyWindow];
        if (!window) {
            NSArray<NSWindow *> *windows = [NSApp windows];
            if (windows.count > 0) window = windows[0];
        }
        if (!window) return 1;
        return ([window occlusionState] & NSWindowOcclusionStateVisible) == 0;
    }

    CGPoint cursor = MacSys_CurrentMouseLocation();

    // Use optionAll so our own always-on-top pet window (layer 101) is always in
    // the list even if it is partially off-screen or its onscreen flag is odd.
    CFArrayRef windowList = CGWindowListCopyWindowInfo(
        kCGWindowListOptionAll | kCGWindowListExcludeDesktopElements,
        kCGNullWindowID);
    if (!windowList) return 1;

    pid_t ownPid = getpid();
    BOOL overOwn = NO;
    NSInteger topWindowPid = -1;
    double topLayer = -1.0;
    for (NSDictionary *info in (__bridge NSArray *)windowList) {
        NSNumber *alphaNum = info[(__bridge NSString *)kCGWindowAlpha];
        if (alphaNum && alphaNum.floatValue <= 0.01f) continue;

        NSDictionary *boundsDict = info[(__bridge NSString *)kCGWindowBounds];
        if (!boundsDict) continue;

        CGRect bounds = CGRectZero;
        if (!CGRectMakeWithDictionaryRepresentation((__bridge CFDictionaryRef)boundsDict, &bounds))
            continue;
        if (bounds.size.width <= 0.0 || bounds.size.height <= 0.0)
            continue;
        if (!CGRectContainsPoint(bounds, cursor))
            continue;

        NSNumber *pidNum = info[(__bridge NSString *)kCGWindowOwnerPID];
        if (pidNum && pidNum.integerValue == (NSInteger)ownPid)
            overOwn = YES;
        double layer = [info[(__bridge NSString *)kCGWindowLayer] doubleValue];
        if (layer > topLayer) {
            topLayer = layer;
            topWindowPid = pidNum ? pidNum.integerValue : -1;
        }
    }
    CFRelease(windowList);

    if (overOwn) return 0;      // cursor is over the pet → not occluded
    if (topWindowPid < 0) return 1;
    return topWindowPid == (NSInteger)ownPid ? 0 : 1;
}

#pragma mark - Running applications

int MacSys_GetRunningAppCount(void)
{
    return (int)[[NSWorkspace sharedWorkspace] runningApplications].count;
}

int MacSys_GetRunningAppName(int index, char *buf, int bufLen)
{
    if (!buf || bufLen <= 0) return -1;
    buf[0] = '\0';

    NSArray<NSRunningApplication *> *apps = [[NSWorkspace sharedWorkspace] runningApplications];
    if (index < 0 || index >= (int)apps.count) return -1;

    NSRunningApplication *app = apps[index];
    NSString *name = app.localizedName;
    if (!name || name.length == 0) name = app.executableURL.lastPathComponent;
    if (!name || name.length == 0) return -1;

    const char *utf8 = name.UTF8String;
    if (!utf8) return -1;
    strncpy(buf, utf8, (size_t)bufLen - 1);
    buf[bufLen - 1] = '\0';
    return 0;
}

#pragma mark - Screen capture

int MacSys_IsScreenCaptureAuthorized(void)
{
    if (@available(macOS 10.15, *)) {
        return CGPreflightScreenCaptureAccess();
    }
    return 1;
}

void MacSys_RequestScreenCaptureAuthorization(void)
{
    if (@available(macOS 10.15, *)) {
        CGRequestScreenCaptureAccess();
    }
}

static CGContextRef MacSys_CreateTargetContext(int targetW, int targetH, uint8_t *buffer)
{
    memset(buffer, 0, (size_t)targetW * (size_t)targetH * 4);
    CGColorSpaceRef colorSpace = CGColorSpaceCreateDeviceRGB();
    CGContextRef ctx = CGBitmapContextCreate(buffer, targetW, targetH, 8, targetW * 4,
                                             colorSpace,
                                             kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little);
    CGColorSpaceRelease(colorSpace);
    if (!ctx) return NULL;

    CGContextTranslateCTM(ctx, 0, targetH);
    CGContextScaleCTM(ctx, 1, -1);
    return ctx;
}

static CGImageRef MacSys_SCKCaptureDisplayImage(CGDirectDisplayID displayID)
{
#if __has_include(<ScreenCaptureKit/ScreenCaptureKit.h>)
    if (@available(macOS 14.0, *)) {
        __block CGImageRef result = NULL;
        dispatch_semaphore_t sem = dispatch_semaphore_create(0);

        [SCShareableContent getShareableContentExcludingDesktopWindows:YES
                                                  onScreenWindowsOnly:YES
                                                  completionHandler:^(SCShareableContent *content, NSError *error) {
            if (!content || error) {
                dispatch_semaphore_signal(sem);
                return;
            }

            SCDisplay *display = nil;
            for (SCDisplay *candidate in content.displays) {
                if (candidate.displayID == displayID) {
                    display = candidate;
                    break;
                }
            }
            if (!display) {
                dispatch_semaphore_signal(sem);
                return;
            }

            pid_t ownPid = getpid();
            NSMutableArray<SCWindow *> *ownWindows = [NSMutableArray array];
            for (SCWindow *window in content.windows) {
                if (!window.isOnScreen) continue;
                SCRunningApplication *app = window.owningApplication;
                if (app && app.processID == ownPid && CGRectIntersectsRect(window.frame, display.frame)) {
                    [ownWindows addObject:window];
                }
            }

            SCContentFilter *filter = [[SCContentFilter alloc] initWithDisplay:display excludingWindows:ownWindows];
            SCStreamConfiguration *config = [[SCStreamConfiguration alloc] init];
            config.width = CGDisplayPixelsWide(displayID);
            config.height = CGDisplayPixelsHigh(displayID);
            config.showsCursor = NO;

            [SCScreenshotManager captureImageWithFilter:filter
                                          configuration:config
                                      completionHandler:^(CGImageRef image, NSError *captureError) {
                if (image) result = CGImageRetain(image);
                dispatch_semaphore_signal(sem);
            }];
        }];

        long wait = dispatch_semaphore_wait(sem, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(3.0 * NSEC_PER_SEC)));
        if (wait != 0) {
            if (result) CGImageRelease(result);
            return NULL;
        }
        return result;
    }
#endif
    return NULL;
}

static CGImageRef MacSys_CreateImageFromIOSurface(IOSurfaceRef surface)
{
    if (!surface) return NULL;

    size_t w = IOSurfaceGetWidth(surface);
    size_t h = IOSurfaceGetHeight(surface);
    size_t bytesPerRow = IOSurfaceGetBytesPerRow(surface);
    if (w == 0 || h == 0 || bytesPerRow == 0) return NULL;

    kern_return_t kr = IOSurfaceLock(surface, kIOSurfaceLockReadOnly, NULL);
    if (kr != 0) return NULL;

    void *base = IOSurfaceGetBaseAddress(surface);
    if (!base) {
        IOSurfaceUnlock(surface, kIOSurfaceLockReadOnly, NULL);
        return NULL;
    }

    CGColorSpaceRef colorSpace = CGColorSpaceCreateDeviceRGB();
    CGContextRef ctx = CGBitmapContextCreate(base, w, h, 8, bytesPerRow, colorSpace,
                                             kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little);
    CGImageRef image = ctx ? CGBitmapContextCreateImage(ctx) : NULL;
    if (ctx) CGContextRelease(ctx);
    CGColorSpaceRelease(colorSpace);

    IOSurfaceUnlock(surface, kIOSurfaceLockReadOnly, NULL);
    return image;
}

// CGDisplayStream was obsoleted in macOS 15 (use ScreenCaptureKit instead) and
// is compile-unavailable when targeting 15+. Keep it only for deployments < 15
// where it is still available as a fallback.
#if __MAC_OS_X_VERSION_MIN_REQUIRED < 150000
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
static CGImageRef MacSys_DisplayStreamCaptureImage(CGDirectDisplayID displayID)
{
    size_t w = CGDisplayPixelsWide(displayID);
    size_t h = CGDisplayPixelsHigh(displayID);
    if (w == 0 || h == 0) return NULL;

    __block IOSurfaceRef frame = NULL;
    dispatch_semaphore_t sem = dispatch_semaphore_create(0);
    dispatch_queue_t queue = dispatch_queue_create("com.shinymoon.mateengine.displaystream", DISPATCH_QUEUE_SERIAL);

    NSDictionary *properties = @{
        (__bridge NSString *)kCGDisplayStreamShowCursor : @NO,
        (__bridge NSString *)kCGDisplayStreamQueueDepth : @2,
    };

    CGDisplayStreamRef stream = CGDisplayStreamCreateWithDispatchQueue(
        displayID, w, h, kCVPixelFormatType_32BGRA,
        (__bridge CFDictionaryRef)properties, queue,
        ^(CGDisplayStreamFrameStatus status, uint64_t displayTime,
          IOSurfaceRef frameSurface, CGDisplayStreamUpdateRef updateRef) {
            (void)displayTime;
            (void)updateRef;
            if (status == kCGDisplayStreamFrameStatusFrameComplete && frameSurface) {
                IOSurfaceIncrementUseCount(frameSurface);
                frame = (IOSurfaceRef)CFRetain(frameSurface);
            }
            dispatch_semaphore_signal(sem);
        });
    if (!stream) return NULL;

    CGError startErr = CGDisplayStreamStart(stream);
    long wait = dispatch_semaphore_wait(sem, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(3.0 * NSEC_PER_SEC)));
    CGDisplayStreamStop(stream);
    CFRelease(stream);

    if (startErr != kCGErrorSuccess || wait != 0 || !frame) return NULL;

    CGImageRef image = MacSys_CreateImageFromIOSurface(frame);
    IOSurfaceDecrementUseCount(frame);
    CFRelease(frame);
    return image;
}
#pragma clang diagnostic pop
#endif // __MAC_OS_X_VERSION_MIN_REQUIRED < 150000

static dispatch_queue_t gCaptureQueue = nil;
static uint8_t *gCachedBuffer = NULL;
static int gCachedW = 0, gCachedH = 0;
static BOOL gIsCapturing = NO;

int MacSys_CaptureDesktop(int targetW, int targetH, uint8_t *buffer)
{
    if (!buffer || targetW <= 0 || targetH <= 0) return 0;
    if (!MacSys_IsScreenCaptureAuthorized()) return 0;

    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        gCaptureQueue = dispatch_queue_create("com.shinymoon.mateengine.capture", DISPATCH_QUEUE_SERIAL);
    });

    if (gCachedBuffer && gCachedW == targetW && gCachedH == targetH) {
        memcpy(buffer, gCachedBuffer, targetW * targetH * 4);
    } else {
        memset(buffer, 0, targetW * targetH * 4);
    }

    if (!gIsCapturing) {
        gIsCapturing = YES;
        dispatch_async(gCaptureQueue, ^{
            if (!gCachedBuffer || gCachedW != targetW || gCachedH != targetH) {
                if (gCachedBuffer) free(gCachedBuffer);
                gCachedBuffer = (uint8_t *)malloc(targetW * targetH * 4);
                gCachedW = targetW;
                gCachedH = targetH;
            }

            CGDirectDisplayID displays[16];
            uint32_t displayCount = 0;
            CGError err = CGGetActiveDisplayList(16, displays, &displayCount);
            if (err == kCGErrorSuccess && displayCount > 0) {
                CGRect unionRect = CGRectZero;
                BOOL haveUnion = NO;
                for (uint32_t i = 0; i < displayCount; i++) {
                    CGRect b = CGDisplayBounds(displays[i]);
                    if (!haveUnion) { unionRect = b; haveUnion = YES; }
                    else { unionRect = CGRectUnion(unionRect, b); }
                }

                if (haveUnion && unionRect.size.width > 0 && unionRect.size.height > 0) {
                    CGContextRef ctx = MacSys_CreateTargetContext(targetW, targetH, gCachedBuffer);
                    if (ctx) {
                        for (uint32_t i = 0; i < displayCount; i++) {
                            CGImageRef image = MacSys_SCKCaptureDisplayImage(displays[i]);
#if __MAC_OS_X_VERSION_MIN_REQUIRED < 150000
                            if (!image) image = MacSys_DisplayStreamCaptureImage(displays[i]);
                            if (!image) image = CGDisplayCreateImage(displays[i]);
#endif
                            if (image) {
                                CGRect db = CGDisplayBounds(displays[i]);
                                CGRect dest = CGRectMake(
                                    (db.origin.x - unionRect.origin.x) / unionRect.size.width * targetW,
                                    (db.origin.y - unionRect.origin.y) / unionRect.size.height * targetH,
                                    db.size.width / unionRect.size.width * targetW,
                                    db.size.height / unionRect.size.height * targetH);
                                CGContextDrawImage(ctx, dest, image);
                                CGImageRelease(image);
                            }
                        }
                        CGContextRelease(ctx);
                    }
                }
            }
            gIsCapturing = NO;
        });
    }

    return 1;
}

#pragma mark - Login item

void MacSys_SetLoginItemEnabled(int enable)
{
#if __MAC_OS_X_VERSION_MAX_ALLOWED >= 130000
    if (@available(macOS 13.0, *)) {
        SMAppService *service = [SMAppService mainAppService];
        NSError *error = nil;
        if (enable) {
            [service registerAndReturnError:&error];
        } else {
            [service unregisterAndReturnError:&error];
        }
        if (!error) return;
    }
#endif
    MacSys_SetLaunchAgentEnabled(enable != 0);
}

int MacSys_IsLoginItemEnabled(void)
{
#if __MAC_OS_X_VERSION_MAX_ALLOWED >= 130000
    if (@available(macOS 13.0, *)) {
        SMAppServiceStatus status = [[SMAppService mainAppService] status];
        if (status == SMAppServiceStatusEnabled || status == SMAppServiceStatusRequiresApproval) {
            return 1;
        }
    }
#endif
    return [[NSFileManager defaultManager] fileExistsAtPath:MacSys_LaunchAgentPath()] ? 1 : 0;
}

#pragma mark - Memory

uint64_t MacSys_RelieveMemory(void)
{
    return (uint64_t)malloc_zone_pressure_relief(malloc_default_zone(), 0);
}
