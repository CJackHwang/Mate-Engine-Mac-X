#import <Cocoa/Cocoa.h>
#import <objc/runtime.h>

// Swizzle NSWindow's constrainFrameRect:toScreen: to prevent macOS from
// forcing the window back below the menu bar when it loses focus.
static NSRect swizzled_constrainFrameRect(id self, SEL _cmd, NSRect frameRect, NSScreen *screen)
{
    return frameRect;
}

static id gActivity = nil;

void MacWindowFix_Install(void)
{
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        Method original = class_getInstanceMethod([NSWindow class],
                            @selector(constrainFrameRect:toScreen:));
        if (original) {
            method_setImplementation(original,
                (IMP)swizzled_constrainFrameRect);
        }
        
        NSActivityOptions options = NSActivityUserInitiatedAllowingIdleSystemSleep | NSActivityLatencyCritical;
        gActivity = [[NSProcessInfo processInfo] beginActivityWithOptions:options reason:@"High FPS requirement"];
    });
}
