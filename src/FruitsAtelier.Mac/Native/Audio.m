#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <mach/mach_time.h>
#include <math.h>
#include <stdint.h>

// Music owns its time-pitch unit; hitsounds use their independent engine.
@interface FAMusic : NSObject
@property(nonatomic, strong) AVAudioEngine *engine;
@property(nonatomic, strong) AVAudioPlayerNode *node;
@property(nonatomic, strong) AVAudioUnitTimePitch *tempo;
@property(nonatomic, strong) AVAudioFile *file;
@property(nonatomic) double position;
@property(nonatomic) double start;
@property(nonatomic) BOOL playing;
@end
@implementation FAMusic
@end

static double hostTime(void) { return [AVAudioTime secondsForHostTime:mach_absolute_time()]; }
static double duration(FAMusic *music) { return music.file.length / music.file.processingFormat.sampleRate; }
static double position(FAMusic *music) {
    if (!music.playing) return music.position;
    // The output render timestamp stops advancing when the device stops rendering.
    AVAudioTime *render = music.engine.outputNode.lastRenderTime;
    double now = render.isHostTimeValid ? [AVAudioTime secondsForHostTime:render.hostTime] : music.start;
    return fmin(duration(music), music.position + fmax(0, now - music.start) * music.tempo.rate);
}
static void pauseMusic(FAMusic *music) {
    music.position = position(music);
    music.playing = NO;
    [music.node stop];
    [music.tempo reset];
}
void *fa_audio_open(const char *path, char *message, int capacity) {
    @autoreleasepool {
        NSError *error = nil;
        FAMusic *music = [FAMusic new];
        music.file = [[AVAudioFile alloc] initForReading:[NSURL fileURLWithPath:[NSString stringWithUTF8String:path]] error:&error];
        if (music.file) {
            music.engine = [AVAudioEngine new];
            music.node = [AVAudioPlayerNode new];
            music.tempo = [AVAudioUnitTimePitch new];
            music.tempo.rate = 1;
            music.tempo.pitch = 0;
            music.tempo.bypass = YES;
            [music.engine attachNode:music.node];
            [music.engine attachNode:music.tempo];
            [music.engine connect:music.node to:music.tempo format:music.file.processingFormat];
            [music.engine connect:music.tempo to:music.engine.mainMixerNode format:music.file.processingFormat];
            [music.engine prepare];
            if ([music.engine startAndReturnError:&error]) return (__bridge_retained void *)music;
        }
        snprintf(message, capacity, "%s", (error.localizedDescription ?: @"Audio preparation failed").UTF8String);
        return NULL;
    }
}
void fa_audio_close(void *handle) {
    @autoreleasepool { FAMusic *music = (__bridge_transfer FAMusic *)handle; [music.node stop]; [music.engine stop]; }
}
void fa_audio_pause(void *handle) { pauseMusic((__bridge FAMusic *)handle); }
void fa_audio_seek(void *handle, double seconds) {
    FAMusic *music = (__bridge FAMusic *)handle;
    pauseMusic(music); music.position = fmax(0, fmin(duration(music), seconds));
}
double fa_audio_position(void *handle) { return position((__bridge FAMusic *)handle); }
double fa_audio_duration(void *handle) { return duration((__bridge FAMusic *)handle); }
int fa_audio_playing(void *handle) {
    FAMusic *music = (__bridge FAMusic *)handle;
    return music.playing && music.engine.isRunning && position(music) < duration(music);
}
void fa_audio_volume(void *handle, float volume) { [(__bridge FAMusic *)handle node].volume = volume; }
double fa_audio_device_time(void *handle) { return hostTime(); }
int fa_audio_play_at(void *handle, double time) {
    FAMusic *music = (__bridge FAMusic *)handle;
    AVAudioFramePosition frame = (AVAudioFramePosition)llround(music.position * music.file.processingFormat.sampleRate);
    AVAudioFramePosition remaining = music.file.length - frame;
    if (remaining <= 0 || remaining > UINT32_MAX) return 0;
    NSError *error = nil;
    if (!music.engine.isRunning && ![music.engine startAndReturnError:&error]) return 0;
    [music.node scheduleSegment:music.file startingFrame:frame frameCount:(AVAudioFrameCount)remaining atTime:nil completionHandler:nil];
    music.start = time;
    [music.node playAtTime:[AVAudioTime timeWithHostTime:[AVAudioTime hostTimeForSeconds:time]]];
    music.playing = YES;
    return 1;
}
int fa_audio_rearm(void *handle, double seconds) {
    fa_audio_seek(handle, seconds);
    return 1;
}
void fa_audio_rate(void *handle, float rate) {
    FAMusic *music = (__bridge FAMusic *)handle;
    music.tempo.rate = rate; music.tempo.bypass = rate == 1;
}
