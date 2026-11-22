#import <AVFoundation/AVFoundation.h>
#import <UIKit/UIKit.h>
#include <algorithm>
#include <atomic>
#include <cstring>
#include <cmath>
#include <vector>

namespace {
AVAudioEngine *engine;
std::vector<float> samples;
std::atomic<int> available{0};
std::atomic<int> permission{-1};
std::atomic<float> nativePeak{0};
std::atomic<int> backgrounded{0};
std::atomic<int> callbackCount{0};
std::atomic<int> droppedFrames{0};
std::atomic<long long> lastSampleEnd{-1};
std::atomic<long long> sampleGapFrames{0};
id backgroundObserver;

void LogAudioSession(NSString *stage) {
    AVAudioSession *session = AVAudioSession.sharedInstance;
    int muted = -1;
    if (@available(iOS 17.0, *)) muted = AVAudioApplication.sharedInstance.isInputMuted ? 1 : 0;
    NSLog(@"Chipi audio session %@: category=%@ mode=%@ permission=%lu muted=%d inputAvailable=%d gain=%.3f outputVolume=%.3f inputs=%@ outputs=%@",
          stage, session.category, session.mode, (unsigned long)session.recordPermission, muted,
          session.isInputAvailable, session.inputGain, session.outputVolume, session.currentRoute.inputs, session.currentRoute.outputs);
}
}

extern "C" void ChipiMicWatchBackground() {
    backgrounded.store(UIApplication.sharedApplication.applicationState == UIApplicationStateBackground ? 1 : 0);
    if (!backgroundObserver) {
        backgroundObserver = [NSNotificationCenter.defaultCenter addObserverForName:UIApplicationDidEnterBackgroundNotification
            object:nil queue:nil usingBlock:^(NSNotification *) { backgrounded.store(1); }];
    }
}

extern "C" int ChipiMicWasBackgrounded() {
    return backgrounded.load();
}

extern "C" int ChipiAudioPlayback() {
    AVAudioSession *session = AVAudioSession.sharedInstance;
    NSError *error = nil;
    if (![session setCategory:AVAudioSessionCategoryPlayback mode:AVAudioSessionModeDefault options:0 error:&error] ||
        ![session setActive:YES error:&error]) {
        NSLog(@"Chipi playback audio session failed: %@ (%ld)", error.domain, (long)error.code);
        return 0;
    }
    LogAudioSession(@"playback");
    return 1;
}

extern "C" void ChipiMicRequestPermission() {
    permission.store(-1);
    void (^completion)(BOOL) = ^(BOOL granted) { permission.store(granted ? 1 : 0); };
    if (@available(iOS 17.0, *))
        [AVAudioApplication requestRecordPermissionWithCompletionHandler:completion];
    else
        [AVAudioSession.sharedInstance requestRecordPermission:completion];
}

extern "C" int ChipiMicPermission() {
    return permission.load();
}

extern "C" void ChipiMicStop() {
    if (!engine) return;
    [engine stop];
        NSLog(@"Chipi microphone native capture ended: frames=%d, peak=%.6f, callbacks=%d, droppedFrames=%d, sampleGapFrames=%lld",
            available.load(), nativePeak.load(), callbackCount.load(), droppedFrames.load(), sampleGapFrames.load());
    LogAudioSession(@"stop");
    [engine.inputNode removeTapOnBus:0];
#if !__has_feature(objc_arc)
    [engine release];
#endif
    engine = nil;
}

extern "C" int ChipiMicStart(int seconds) {
    ChipiMicStop();
    available.store(0, std::memory_order_relaxed);
    nativePeak.store(0, std::memory_order_relaxed);
    callbackCount.store(0, std::memory_order_relaxed);
    droppedFrames.store(0, std::memory_order_relaxed);
    lastSampleEnd.store(-1, std::memory_order_relaxed);
    sampleGapFrames.store(0, std::memory_order_relaxed);
    AVAudioSession *session = AVAudioSession.sharedInstance;
    NSError *error = nil;
    if (![session setCategory:AVAudioSessionCategoryPlayAndRecord
                                 mode:AVAudioSessionModeDefault
                             options:AVAudioSessionCategoryOptionDefaultToSpeaker |
                                        AVAudioSessionCategoryOptionAllowBluetooth |
                                        AVAudioSessionCategoryOptionAllowBluetoothA2DP
                        error:&error] || ![session setActive:YES error:&error]) {
        NSLog(@"Chipi microphone audio session failed: %@ (%ld)", error.domain, (long)error.code);
        return 0;
    }
    if (@available(iOS 17.0, *)) {
        if (AVAudioApplication.sharedInstance.isInputMuted) {
            NSLog(@"Chipi microphone is muted by iOS");
            return -1;
        }
    }
    AVAudioSessionPortDescription *port = session.currentRoute.inputs.firstObject;
    NSLog(@"Chipi microphone available data sources: %@", port.dataSources);
    if ([NSProcessInfo.processInfo.environment[@"CHIPI_MIC_FRONT"] isEqualToString:@"1"]) {
        for (AVAudioSessionDataSourceDescription *source in port.dataSources) {
            if ([source.orientation isEqualToString:AVAudioSessionOrientationFront]) {
                BOOL selected = [port setPreferredDataSource:source error:&error];
                NSLog(@"Chipi microphone front route selected=%d error=%@", selected, error);
                break;
            }
        }
    }
    engine = [[AVAudioEngine alloc] init];
    AVAudioInputNode *input = engine.inputNode;
    AVAudioFormat *format = [input outputFormatForBus:0];
    int rate = (int)format.sampleRate;
    if (rate < 8000 || rate > 96000 || format.channelCount == 0 ||
        format.commonFormat != AVAudioPCMFormatFloat32 || format.isInterleaved) {
        NSLog(@"Chipi microphone input format is unavailable");
#if !__has_feature(objc_arc)
        [engine release];
#endif
        engine = nil;
        return 0;
    }

    samples.resize((size_t)rate * seconds);
    [input installTapOnBus:0 bufferSize:1024 format:format
                     block:^(AVAudioPCMBuffer *buffer, AVAudioTime *when) {
        callbackCount.fetch_add(1, std::memory_order_relaxed);
        if (when.isSampleTimeValid) {
            long long expected = lastSampleEnd.exchange(when.sampleTime + buffer.frameLength, std::memory_order_relaxed);
            if (expected >= 0 && when.sampleTime > expected)
                sampleGapFrames.fetch_add(when.sampleTime - expected, std::memory_order_relaxed);
        }
        const float * const *channels = buffer.floatChannelData;
        if (!channels) return;
        int start = available.load(std::memory_order_relaxed);
        int count = std::min((int)buffer.frameLength, (int)samples.size() - start);
        if (count < (int)buffer.frameLength)
            droppedFrames.fetch_add((int)buffer.frameLength - std::max(0, count), std::memory_order_relaxed);
        if (count <= 0) return;
        const float *selected = channels[0];
        float strongest = 0;
        for (AVAudioChannelCount channel = 0; channel < buffer.format.channelCount; channel++) {
            const float *inputSamples = channels[channel];
            if (!inputSamples) continue;
            float peak = 0;
            for (int i = 0; i < count; i++) peak = std::max(peak, std::abs(inputSamples[i]));
            if (peak > strongest) {
                strongest = peak;
                selected = inputSamples;
            }
        }
        if (!selected) return;
        nativePeak.store(std::max(strongest, nativePeak.load(std::memory_order_relaxed)),
                         std::memory_order_relaxed);
        memcpy(samples.data() + start, selected, (size_t)count * sizeof(float));
        available.store(start + count, std::memory_order_release);
    }];
    [engine prepare];
    if (![engine startAndReturnError:&error]) {
        NSLog(@"Chipi microphone start failed: %@ (%ld)", error.domain, (long)error.code);
        ChipiMicStop();
        return 0;
    }
    NSLog(@"Chipi microphone native route: %@, %d Hz, %u channels, input available=%d",
          session.currentRoute.inputs.firstObject.portType, rate, (unsigned)format.channelCount,
          session.isInputAvailable);
    LogAudioSession(@"start");
    return rate;
}

extern "C" int ChipiMicPosition() {
    return available.load(std::memory_order_acquire);
}

extern "C" int ChipiMicRead(int offset, float *output, int count) {
    if (offset < 0 || count < 0 || offset + count > ChipiMicPosition()) return 0;
    memcpy(output, samples.data() + offset, (size_t)count * sizeof(float));
    return 1;
}
