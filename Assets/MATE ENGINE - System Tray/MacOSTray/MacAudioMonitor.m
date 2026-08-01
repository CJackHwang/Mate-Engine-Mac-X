#import <AVFoundation/AVFoundation.h>
#import <CoreAudio/CoreAudio.h>
#import <Foundation/Foundation.h>

static AVAudioEngine* gEngine = nil;
static float gPeakLevel = 0.0f;
static BOOL gRunning = NO;

void MacAudio_Start(void)
{
    if (gRunning) return;

    gEngine = [[AVAudioEngine alloc] init];
    AVAudioMixerNode* mainMixer = gEngine.mainMixerNode;

    // Install tap on the main mixer output to sample audio level
    AVAudioFormat* format = [mainMixer outputFormatForBus:0];
    [mainMixer installTapOnBus:0 bufferSize:1024 format:format block:^(AVAudioPCMBuffer* buffer, AVAudioTime* when) {
        float peak = 0.0f;
        AVAudioChannelCount channels = buffer.format.channelCount;
        for (AVAudioChannelCount ch = 0; ch < channels; ch++) {
            float* data = buffer.floatChannelData[ch];
            AVAudioFrameCount frames = buffer.frameLength;
            for (AVAudioFrameCount i = 0; i < frames; i++) {
                float v = fabsf(data[i]);
                if (v > peak) peak = v;
            }
        }
        gPeakLevel = peak;
    }];

    NSError* error = nil;
    [gEngine startAndReturnError:&error];
    if (error) {
        NSLog(@"[MacAudioMonitor] AVAudioEngine start error: %@", error);
        gEngine = nil;
        return;
    }
    gRunning = YES;
    NSLog(@"[MacAudioMonitor] AVAudioEngine tap started.");
}

void MacAudio_Stop(void)
{
    if (!gRunning || gEngine == nil) return;
    [gEngine.mainMixerNode removeTapOnBus:0];
    [gEngine stop];
    gEngine = nil;
    gRunning = NO;
    gPeakLevel = 0.0f;
}

// Returns 1 if audio peak level exceeds threshold, 0 otherwise.
int MacAudio_IsOutputActive(void)
{
    if (!gRunning) MacAudio_Start();
    return gPeakLevel > 0.01f ? 1 : 0;
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
