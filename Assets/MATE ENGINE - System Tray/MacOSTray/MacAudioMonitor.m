#import <AVFoundation/AVFoundation.h>
#import <CoreAudio/CoreAudio.h>
#import <CoreMedia/CoreMedia.h>
#import <CoreGraphics/CoreGraphics.h>
#import <Foundation/Foundation.h>
#if __has_include(<ScreenCaptureKit/ScreenCaptureKit.h>)
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#endif

// ─────────────────────────────────────────────────────────────────────────────
// System-wide audio capture via ScreenCaptureKit (macOS 13+).
//
// The old implementation tapped an empty AVAudioEngine mixer, which can only
// hear audio routed through that engine — i.e. nothing — so macOS could never
// react to music from other apps. An SCStream with capturesAudio=YES captures
// the whole system output mix (any music player), which is what "dance to the
// music the user is playing" actually needs.
//
// Exposed C functions (see MacAudioMonitorBinding.cs):
//   MacAudio_Start()                 — start the SCStream capture (lazy, retries)
//   MacAudio_Stop()                  — stop and release everything
//   MacAudio_IsOutputActive()        — 1 = sound above threshold,
//                                      0 = capture running but silent,
//                                     -1 = capture unavailable (no permission /
//                                          macOS < 13 / failed to start)
//   MacAudio_SystemCaptureAvailable()— 1 if the capture stream is running
//   MacAudio_HasCapturePermission()  — 1 if Screen Recording is authorized
//   MacAudio_GetDefaultDeviceName()  — unchanged (CoreAudio device name)
// ─────────────────────────────────────────────────────────────────────────────

static volatile float gPeakLevel = 0.0f; // written on the audio queue, read from the poll thread
static id gSystemAudioStream = nil;      // SCStream, kept alive while capturing
static id gAudioOutputDelegate = nil;    // SCStreamOutput + SCStreamDelegate
static dispatch_queue_t gAudioQueue = nil;
static int gCaptureState = 0;            // 0 = not started, 1 = starting, 2 = running, 3 = unavailable
static BOOL gRequestedPermission = NO;

#if __has_include(<ScreenCaptureKit/ScreenCaptureKit.h>)
@interface MacAudioTapDelegate : NSObject <SCStreamOutput, SCStreamDelegate>
@end

@implementation MacAudioTapDelegate

- (void)stream:(SCStream *)stream didOutputSampleBuffer:(CMSampleBufferRef)sampleBuffer ofType:(SCStreamOutputType)type API_AVAILABLE(macos(13.0))
{
    (void)stream;
    if (@available(macOS 13.0, *)) {
        if (type != SCStreamOutputTypeAudio || sampleBuffer == NULL) return;

        // SCK audio sample buffers are PLAIN data buffers (no AudioBufferList),
        // so read the raw CMBlockBuffer and scan all Float32 samples. This works
        // for both interleaved and planar layouts since every sample is present.
        CMBlockBufferRef dataBuffer = CMSampleBufferGetDataBuffer(sampleBuffer);
        if (!dataBuffer) {
            NSLog(@"[MacAudioMonitor] no data buffer in audio sample");
            return;
        }
        size_t lenAtOffset = 0;
        size_t totalLen = 0;
        char *bytes = NULL;
        OSStatus status = CMBlockBufferGetDataPointer(dataBuffer, 0, &lenAtOffset, &totalLen, &bytes);
        if (status != noErr || bytes == NULL || lenAtOffset == 0) {
            NSLog(@"[MacAudioMonitor] data buffer read failed: status=%d len=%zu", (int)status, lenAtOffset);
            return;
        }

        float peak = 0.0f;
        const float *samples = (const float *)bytes;
        size_t count = lenAtOffset / sizeof(float);
        for (size_t j = 0; j < count; j++) {
            float v = fabsf(samples[j]);
            if (v > peak) peak = v;
        }

        gPeakLevel = peak;
    }
}

- (void)stream:(SCStream *)stream didStopWithError:(NSError *)error API_AVAILABLE(macos(13.0))
{
    (void)stream;
    if (error) {
        NSLog(@"[MacAudioMonitor] SCStream stopped with error: %@", error);
    }
    gCaptureState = 0; // reset so the next poll can retry
}

@end
#endif

static void MacAudio_StartCapture(void)
{
#if __has_include(<ScreenCaptureKit/ScreenCaptureKit.h>)
    if (@available(macOS 13.0, *)) {
        if (gCaptureState != 0) return;

        if (!CGPreflightScreenCaptureAccess()) {
            // One-time prompt for Screen Recording permission (main thread).
            NSLog(@"[MacAudioMonitor] Screen Recording permission missing; requesting...");
            if (!gRequestedPermission) {
                gRequestedPermission = YES;
                CGRequestScreenCaptureAccess();
            }
            gCaptureState = 3; // unavailable for now; retried lazily if granted later
            return;
        }

        gCaptureState = 1;
        [SCShareableContent getShareableContentExcludingDesktopWindows:YES
                                                  onScreenWindowsOnly:NO
                                                  completionHandler:^(SCShareableContent *content, NSError *error) {
            if (!content || error) {
                NSLog(@"[MacAudioMonitor] SCShareableContent error: %@", error);
                gCaptureState = 3;
                return;
            }
            SCDisplay *display = content.displays.firstObject;
            if (!display) {
                NSLog(@"[MacAudioMonitor] No display found for system audio capture");
                gCaptureState = 3;
                return;
            }

            SCContentFilter *filter = [[SCContentFilter alloc] initWithDisplay:display excludingWindows:@[]];
            SCStreamConfiguration *config = [[SCStreamConfiguration alloc] init];
            config.width = 2;   // audio-only; minimal video to satisfy SCK
            config.height = 2;
            config.capturesAudio = YES;
            config.excludesCurrentProcessAudio = YES; // don't self-trigger on the avatar's own TTS/sounds
            config.sampleRate = 48000;
            config.channelCount = 2;

            gAudioQueue = dispatch_queue_create("com.shinymoon.mateengine.audiocapture", DISPATCH_QUEUE_SERIAL);
            gAudioOutputDelegate = [[MacAudioTapDelegate alloc] init];
            SCStream *stream = [[SCStream alloc] initWithFilter:filter configuration:config delegate:gAudioOutputDelegate];
            if (!stream) {
                NSLog(@"[MacAudioMonitor] Failed to create SCStream");
                gCaptureState = 3;
                return;
            }
            gSystemAudioStream = stream;
            NSError *outputError = nil;
            [stream addStreamOutput:gAudioOutputDelegate
                               type:SCStreamOutputTypeAudio
                 sampleHandlerQueue:gAudioQueue
                              error:&outputError];
            if (outputError) {
                NSLog(@"[MacAudioMonitor] addStreamOutput error: %@", outputError);
                gSystemAudioStream = nil;
                gCaptureState = 3;
                return;
            }
            [stream startCaptureWithCompletionHandler:^(NSError *startError) {
                if (startError) {
                    NSLog(@"[MacAudioMonitor] SCStream start error: %@", startError);
                    gSystemAudioStream = nil;
                    gCaptureState = 3;
                } else {
                    NSLog(@"[MacAudioMonitor] SCStream system audio capture started.");
                    gCaptureState = 2;
                }
            }];
        }];
        return;
    }
#endif
    gCaptureState = 3; // macOS < 13: no SCK audio capture
}

static void MacAudio_StopCapture(void)
{
#if __has_include(<ScreenCaptureKit/ScreenCaptureKit.h>)
    if (@available(macOS 13.0, *)) {
        SCStream *stream = (SCStream *)gSystemAudioStream;
        if (stream) {
            [stream stopCaptureWithCompletionHandler:nil];
        }
    }
#endif
    gSystemAudioStream = nil;
    gAudioOutputDelegate = nil;
    gAudioQueue = nil;
    gCaptureState = 0;
    gPeakLevel = 0.0f;
}

void MacAudio_Start(void)
{
    if (gCaptureState == 2) return;
    // If we were marked unavailable only because permission was missing, retry
    // now that it may have been granted (System Settings → Privacy → Screen Recording).
    if (gCaptureState == 3) {
        if (@available(macOS 13.0, *)) {
            if (CGPreflightScreenCaptureAccess()) {
                gCaptureState = 0;
                MacAudio_StartCapture();
            }
        }
        return;
    }
    MacAudio_StartCapture();
}

void MacAudio_Stop(void)
{
    MacAudio_StopCapture();
}

// 1 = system audio above threshold, 0 = capture running but silent, -1 = unavailable.
int MacAudio_IsOutputActive(void)
{
    if (gCaptureState == 0) MacAudio_Start();
    if (gCaptureState != 2) return -1;
    return gPeakLevel > 0.01f ? 1 : 0;
}

// 1 if the SCStream audio capture is currently running, else 0.
int MacAudio_SystemCaptureAvailable(void)
{
    return (gCaptureState == 2) ? 1 : 0;
}

// 1 if Screen Recording permission is granted.
int MacAudio_HasCapturePermission(void)
{
    if (@available(macOS 10.15, *)) {
        return CGPreflightScreenCaptureAccess() ? 1 : 0;
    }
    return 1;
}

// Returns the name of the default output device.
int MacAudio_GetDefaultDeviceName(char* buf, int bufLen)
{
    if (!buf || bufLen <= 0) return -1;
    buf[0] = '\0';

    AudioObjectPropertyAddress addr = {
        kAudioHardwarePropertyDefaultOutputDevice,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain
    };
    AudioDeviceID deviceID = kAudioObjectUnknown;
    UInt32 dataSize = sizeof(AudioDeviceID);
    OSStatus status = AudioObjectGetPropertyData(kAudioObjectSystemObject, &addr, 0, NULL, &dataSize, &deviceID);
    if (status != noErr || deviceID == kAudioObjectUnknown) {
        strncpy(buf, "<unknown>", bufLen - 1);
        return -1;
    }

    AudioObjectPropertyAddress nameAddr = {
        kAudioObjectPropertyName,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain
    };
    CFStringRef cfName = NULL;
    dataSize = sizeof(CFStringRef);
    status = AudioObjectGetPropertyData(deviceID, &nameAddr, 0, NULL, &dataSize, &cfName);
    if (status != noErr || cfName == NULL) {
        strncpy(buf, "<name error>", bufLen - 1);
        return -1;
    }
    CFStringGetCString(cfName, buf, bufLen, kCFStringEncodingUTF8);
    CFRelease(cfName);
    return 0;
}
