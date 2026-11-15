#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <mach/mach_time.h>
#include <math.h>
#include <stdint.h>

void *fa_waveform_open(const char *path, int *rate, int *channels) {
    @autoreleasepool {
        AVAudioFile *file = [[AVAudioFile alloc] initForReading:[NSURL fileURLWithPath:[NSString stringWithUTF8String:path]]
            commonFormat:AVAudioPCMFormatFloat32 interleaved:NO error:nil];
        if (!file) return NULL;
        *rate = (int)file.processingFormat.sampleRate;
        *channels = (int)file.processingFormat.channelCount;
        return (__bridge_retained void *)file;
    }
}
int fa_waveform_read(void *handle, float *samples, int count) {
    @autoreleasepool {
        AVAudioFile *file = (__bridge AVAudioFile *)handle;
        unsigned channels = file.processingFormat.channelCount;
        AVAudioPCMBuffer *buffer = [[AVAudioPCMBuffer alloc] initWithPCMFormat:file.processingFormat frameCapacity:count / channels];
        if (![file readIntoBuffer:buffer error:nil]) return -1;
        for (unsigned f = 0; f < buffer.frameLength; f++)
            for (unsigned c = 0; c < channels; c++) samples[f * channels + c] = buffer.floatChannelData[c][f];
        return (int)(buffer.frameLength * channels);
    }
}
void fa_waveform_close(void *handle) {
    @autoreleasepool { AVAudioFile *file = (__bridge_transfer AVAudioFile *)handle; (void)file; }
}

// Music PCM is decoded and time-stretched off the render thread. The player
// only presents prepared buffers, so all speeds share the hitsound host clock.
#include <stdatomic.h>
@interface FAMonitor : NSObject {
@public _Atomic double renderedOnset, monitorStart;
@public _Atomic unsigned observedSamples, positiveCrossings;
@public float previousSample;
}
@end
@implementation FAMonitor
@end

@interface FAMusic : NSObject
@property(nonatomic, strong) FAMonitor *monitor;
@property(nonatomic, strong) AVAudioEngine *engine;
@property(nonatomic, strong) AVAudioPlayerNode *node;
@property(nonatomic, strong) AVAudioFile *file;
@property(nonatomic) double position, start, rate;
@property(nonatomic) int64_t queuedFrames;
@property(nonatomic) BOOL playing, monitoring;
@end
@implementation FAMusic
@end

static double hostTime(void) { return [AVAudioTime secondsForHostTime:mach_absolute_time()]; }
static double duration(FAMusic *music) { return music.file.length / music.file.processingFormat.sampleRate; }
static double position(FAMusic *music) {
    if (!music.playing) return music.position;
    AVAudioTime *render = music.engine.outputNode.lastRenderTime;
    // Render callbacks may run ahead of the speaker clock; never publish future PCM.
    double now = render.isHostTimeValid ? fmin(hostTime(), [AVAudioTime secondsForHostTime:render.hostTime]) : music.start;
    return fmin(duration(music), music.position + fmax(0, now - music.start) * music.rate);
}
static void pauseMusic(FAMusic *music) {
    music.position = position(music); music.playing = NO;
    [music.node stop]; music.queuedFrames = 0;
    if (music.monitor) atomic_store(&music.monitor->monitorStart, INFINITY);
}
void *fa_audio_open(const char *path, char *message, int capacity) {
    @autoreleasepool {
        NSError *error = nil;
        FAMusic *music = [FAMusic new]; music.rate = 1;
        music.file = [[AVAudioFile alloc] initForReading:[NSURL fileURLWithPath:[NSString stringWithUTF8String:path]]
            commonFormat:AVAudioPCMFormatFloat32 interleaved:NO error:&error];
        if (music.file) {
            music.engine = [AVAudioEngine new]; music.node = [AVAudioPlayerNode new];
            [music.engine attachNode:music.node];
            [music.engine connect:music.node to:music.engine.mainMixerNode format:music.file.processingFormat];
            [music.engine prepare];
            if ([music.engine startAndReturnError:&error]) return (__bridge_retained void *)music;
        }
        snprintf(message, capacity, "%s", (error.localizedDescription ?: @"Audio preparation failed").UTF8String);
        return NULL;
    }
}
void fa_audio_close(void *handle) {
    @autoreleasepool {
        FAMusic *music = (__bridge_transfer FAMusic *)handle;
        [music.node stop]; [music.engine stop];
        if (music.monitoring) [music.node removeTapOnBus:0];
    }
}
void fa_audio_pause(void *handle) { pauseMusic((__bridge FAMusic *)handle); }
void fa_audio_seek(void *handle, double seconds) {
    FAMusic *music = (__bridge FAMusic *)handle;
    pauseMusic(music); music.position = fmax(INT32_MIN / 1000.0, fmin(duration(music), seconds));
    music.file.framePosition = (AVAudioFramePosition)llround(fmax(0, music.position) * music.file.processingFormat.sampleRate);
}
double fa_audio_position(void *handle) { return position((__bridge FAMusic *)handle); }
double fa_audio_duration(void *handle) { return duration((__bridge FAMusic *)handle); }
int fa_audio_playing(void *handle) {
    FAMusic *music = (__bridge FAMusic *)handle;
    return music.playing && music.engine.isRunning && position(music) < duration(music);
}
void fa_audio_volume(void *handle, float volume) { [(__bridge FAMusic *)handle engine].mainMixerNode.outputVolume = volume; }
double fa_audio_device_time(void *handle) { return hostTime(); }
int fa_audio_play_at(void *handle, double time) {
    FAMusic *music = (__bridge FAMusic *)handle;
    if (music.queuedFrames == 0) return 0;
    NSError *error = nil;
    if (!music.engine.isRunning && ![music.engine startAndReturnError:&error]) return 0;
    music.start = time;
    double audioStart = time + fmax(0, -music.position) / music.rate;
    FAMonitor *monitor = music.monitor;
    if (monitor) {
        atomic_store(&monitor->renderedOnset, 0); atomic_store(&monitor->monitorStart, audioStart);
        atomic_store(&monitor->observedSamples, 0); atomic_store(&monitor->positiveCrossings, 0); monitor->previousSample = 0;
    }
    [music.node playAtTime:[AVAudioTime timeWithHostTime:[AVAudioTime hostTimeForSeconds:audioStart]]];
    music.playing = YES;
    return 1;
}
int fa_audio_rearm(void *handle, double seconds) { fa_audio_seek(handle, seconds); return 1; }
void fa_audio_rate(void *handle, float rate, int adjustPitch) { [(__bridge FAMusic *)handle setRate:rate]; }
int fa_audio_channels(void *handle) { return (int)[(__bridge FAMusic *)handle file].processingFormat.channelCount; }
int fa_audio_sample_rate(void *handle) { return (int)[(__bridge FAMusic *)handle file].processingFormat.sampleRate; }
int fa_audio_read(void *handle, float *samples, int frames) {
    @autoreleasepool {
        FAMusic *music = (__bridge FAMusic *)handle;
        AVAudioFramePosition remaining = music.file.length - music.file.framePosition;
        if (remaining <= 0) return 0;
        frames = (int)MIN((AVAudioFramePosition)frames, remaining);
        AVAudioPCMBuffer *buffer = [[AVAudioPCMBuffer alloc] initWithPCMFormat:music.file.processingFormat frameCapacity:frames];
        if (![music.file readIntoBuffer:buffer error:nil]) return -1;
        unsigned channels = buffer.format.channelCount;
        for (unsigned f = 0; f < buffer.frameLength; f++)
            for (unsigned c = 0; c < channels; c++) samples[f * channels + c] = buffer.floatChannelData[c][f];
        return (int)buffer.frameLength;
    }
}
void fa_audio_enqueue(void *handle, const float *samples, int frames) {
    @autoreleasepool {
        FAMusic *music = (__bridge FAMusic *)handle;
        AVAudioPCMBuffer *buffer = [[AVAudioPCMBuffer alloc] initWithPCMFormat:music.file.processingFormat frameCapacity:frames];
        buffer.frameLength = frames;
        unsigned channels = buffer.format.channelCount;
        for (unsigned f = 0; f < frames; f++)
            for (unsigned c = 0; c < channels; c++) buffer.floatChannelData[c][f] = samples[f * channels + c];
        [music.node scheduleBuffer:buffer completionHandler:nil];
        music.queuedFrames += frames;
    }
}
double fa_audio_buffered(void *handle) {
    FAMusic *music = (__bridge FAMusic *)handle;
    AVAudioTime *renderTime = music.node.lastRenderTime;
    AVAudioTime *time = renderTime ? [music.node playerTimeForNodeTime:renderTime] : nil;
    int64_t rendered = time.isSampleTimeValid ? MAX(0, time.sampleTime) : 0;
    return fmax(0, music.queuedFrames - rendered) / music.file.processingFormat.sampleRate;
}
// Opt-in regression probe observes actual PCM before mixer volume/device muting.
void fa_audio_monitor(void *handle) {
    FAMusic *music = (__bridge FAMusic *)handle;
    if (music.monitoring) return;
    music.monitoring = YES;
    // The tap must not retain the engine: its final release can occur on an
    // AVAudioPlayerNode queue, where engine teardown would synchronously deadlock.
    FAMonitor *monitor = [FAMonitor new]; music.monitor = monitor;
    atomic_store(&monitor->monitorStart, INFINITY);
    [music.node installTapOnBus:0 bufferSize:1024 format:nil block:^(AVAudioPCMBuffer *buffer, AVAudioTime *time) {
        if (!time.isHostTimeValid) return;
        double host = [AVAudioTime secondsForHostTime:time.hostTime];
        for (unsigned f = 0; f < buffer.frameLength; f++) {
            double sampleTime = host + f / buffer.format.sampleRate;
            double elapsed = sampleTime - atomic_load(&monitor->monitorStart);
            if (elapsed < 0) continue;
            float value = buffer.floatChannelData[0][f];
            if (atomic_load(&monitor->renderedOnset) == 0 && fabsf(value) > .01f)
                atomic_store(&monitor->renderedOnset, sampleTime);
            if (elapsed >= .1 && elapsed < .6) {
                atomic_fetch_add(&monitor->observedSamples, 1);
                if (monitor->previousSample < 0 && value >= 0) atomic_fetch_add(&monitor->positiveCrossings, 1);
                monitor->previousSample = value;
            }
        }
    }];
}
double fa_audio_rendered_onset(void *handle) {
    FAMonitor *monitor = [(__bridge FAMusic *)handle monitor];
    return monitor ? atomic_load(&monitor->renderedOnset) : 0;
}
double fa_audio_rendered_frequency(void *handle) {
    FAMusic *music = (__bridge FAMusic *)handle;
    FAMonitor *monitor = music.monitor;
    if (!monitor) return 0;
    unsigned frames = atomic_load(&monitor->observedSamples);
    return frames ? atomic_load(&monitor->positiveCrossings) * music.file.processingFormat.sampleRate / frames : 0;
}
