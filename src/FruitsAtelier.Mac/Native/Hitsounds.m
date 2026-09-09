#import <AVFoundation/AVFoundation.h>
#import <mach/mach_time.h>
#include <stdatomic.h>

#define FA_QUEUE 2048
#define FA_VOICES 128
#define FA_RATE 44100.0

typedef struct { const float *data; unsigned length; double start; float volume; unsigned generation; bool rendered; } FAHit;
typedef struct {
    FAHit queue[FA_QUEUE], voices[FA_VOICES];
    _Atomic unsigned read, write, generation, active;
    _Atomic double lastStart, lastDuration, lastRenderedStart;
    double secondsPerTick;
    bool muted;
} FAMix;

// The audio callback performs bounded PCM mixing only: no allocation, I/O, locks or Objective-C calls.
static void render(FAMix *mix, double time, unsigned frames, float *out) {
    memset(out, 0, frames * sizeof(float));
    unsigned generation = atomic_load(&mix->generation);
    unsigned read = atomic_load(&mix->read), write = atomic_load(&mix->write);
    while (read != write) {
        FAHit hit = mix->queue[read]; read = (read + 1) % FA_QUEUE;
        if (hit.generation != generation) continue;
        unsigned slot = 0;
        for (unsigned i = 0; i < FA_VOICES; i++) {
            FAHit v = mix->voices[i];
            if (!v.data || v.generation != generation || v.start + v.length / FA_RATE <= time) { slot = i; break; }
            if (v.start < mix->voices[slot].start) slot = i;
        }
        mix->voices[slot] = hit;
    }
    atomic_store(&mix->read, read);
    unsigned active = 0;
    for (unsigned v = 0; v < FA_VOICES; v++) {
        FAHit hit = mix->voices[v];
        if (!hit.data || hit.generation != generation) continue;
        long offset = llround((time - hit.start) * FA_RATE);
        if (offset >= hit.length) { mix->voices[v].data = NULL; continue; }
        active++;
        unsigned first = offset < 0 ? (unsigned)MIN((long)frames, -offset) : 0;
        unsigned end = (unsigned)MIN((long)frames, (long)hit.length - offset);
        if (!hit.rendered && first < end) {
            atomic_store(&mix->lastRenderedStart, time + first / FA_RATE);
            mix->voices[v].rendered = true;
        }
        for (unsigned f = first; f < end; f++) out[f] += hit.data[offset + f] * hit.volume;
    }
    atomic_store(&mix->active, active);
    for (unsigned f = 0; f < frames; f++) out[f] = mix->muted ? 0 : fmaxf(-1, fminf(1, out[f]));
}

@interface FAHitEngine : NSObject {
@public FAMix *mix;
}
@property AVAudioEngine *engine;
@property AVAudioSourceNode *source;
@end
@implementation FAHitEngine
- (void)dealloc { [_engine stop]; free(mix); }
@end

void *fa_hitsounds_open(int muted) {
    @autoreleasepool {
        FAHitEngine *owner = [FAHitEngine new]; owner->mix = calloc(1, sizeof(FAMix));
        FAMix *mix = owner->mix; mix->muted = muted > 0;
        mach_timebase_info_data_t base; mach_timebase_info(&base);
        mix->secondsPerTick = (double)base.numer / base.denom / 1e9;
        if (muted < 0) return (__bridge_retained void *)owner; // Offline PCM regression.
        owner.engine = [AVAudioEngine new];
        AVAudioFormat *format = [[AVAudioFormat alloc] initStandardFormatWithSampleRate:FA_RATE channels:1];
        owner.source = [[AVAudioSourceNode alloc] initWithFormat:format renderBlock:^OSStatus(BOOL *silent, const AudioTimeStamp *stamp, AVAudioFrameCount frames, AudioBufferList *output) {
            double time = stamp->mHostTime * mix->secondsPerTick;
            render(mix, time, frames, output->mBuffers[0].mData);
            *silent = mix->muted || atomic_load(&mix->active) == 0;
            return noErr;
        }];
        [owner.engine attachNode:owner.source];
        [owner.engine connect:owner.source to:owner.engine.mainMixerNode format:format];
        [owner.engine prepare];
        if (![owner.engine startAndReturnError:nil]) return NULL;
        return (__bridge_retained void *)owner;
    }
}
void fa_hitsounds_close(void *handle) {
    @autoreleasepool { FAHitEngine *owner = (__bridge_transfer FAHitEngine *)handle; [owner.engine stop]; }
}

void *fa_hitsounds_sample(const char *path) {
    @autoreleasepool {
        AVAudioFile *file = [[AVAudioFile alloc] initForReading:[NSURL fileURLWithPath:[NSString stringWithUTF8String:path]] error:nil];
        if (!file || file.length <= 0 || file.length * file.processingFormat.channelCount * 4 > 16 * 1024 * 1024) return NULL;
        AVAudioPCMBuffer *input = [[AVAudioPCMBuffer alloc] initWithPCMFormat:file.processingFormat frameCapacity:(AVAudioFrameCount)file.length];
        if (![file readIntoBuffer:input error:nil]) return NULL;
        AVAudioFormat *format = [[AVAudioFormat alloc] initStandardFormatWithSampleRate:FA_RATE channels:1];
        double frames = ceil(input.frameLength * FA_RATE / input.format.sampleRate) + 64;
        if (frames * 4 > 16 * 1024 * 1024) return NULL;
        AVAudioPCMBuffer *output = [[AVAudioPCMBuffer alloc] initWithPCMFormat:format frameCapacity:(AVAudioFrameCount)frames];
        AVAudioConverter *converter = [[AVAudioConverter alloc] initFromFormat:input.format toFormat:format];
        __block BOOL supplied = NO; NSError *error = nil;
        [converter convertToBuffer:output error:&error withInputFromBlock:^AVAudioBuffer *(AVAudioPacketCount count, AVAudioConverterInputStatus *status) {
            if (supplied) { *status = AVAudioConverterInputStatus_EndOfStream; return nil; }
            supplied = YES; *status = AVAudioConverterInputStatus_HaveData; return input;
        }];
        if (error || !output.frameLength) return NULL;
        return (__bridge_retained void *)output;
    }
}
void fa_hitsounds_sample_close(void *handle) { @autoreleasepool { AVAudioPCMBuffer *buffer = (__bridge_transfer AVAudioPCMBuffer *)handle; (void)buffer; } }
unsigned fa_hitsounds_sample_bytes(void *handle) { return [(__bridge AVAudioPCMBuffer *)handle frameLength] * sizeof(float); }
double fa_audio_host_time(void) { return [AVAudioTime secondsForHostTime:mach_absolute_time()]; }
int fa_hitsounds_schedule(void *handle, void *sample, double start, float volume) {
    FAMix *mix = ((__bridge FAHitEngine *)handle)->mix;
    unsigned write = atomic_load(&mix->write), next = (write + 1) % FA_QUEUE;
    if (next == atomic_load(&mix->read)) return 0;
    AVAudioPCMBuffer *buffer = (__bridge AVAudioPCMBuffer *)sample;
    mix->queue[write] = (FAHit){ buffer.floatChannelData[0], buffer.frameLength, start, volume, atomic_load(&mix->generation), false };
    atomic_store(&mix->lastStart, start); atomic_store(&mix->lastDuration, buffer.frameLength / FA_RATE);
    atomic_store(&mix->write, next);
    return 1;
}
void fa_hitsounds_stop(void *handle) {
    FAMix *mix = ((__bridge FAHitEngine *)handle)->mix;
    atomic_fetch_add(&mix->generation, 1); atomic_store(&mix->active, 0); atomic_store(&mix->lastStart, 0);
}
unsigned fa_hitsounds_active(void *handle) {
    FAMix *mix = ((__bridge FAHitEngine *)handle)->mix;
    if (atomic_load(&mix->lastStart) == 0) return 0;
    return atomic_load(&mix->active) + (atomic_load(&mix->write) != atomic_load(&mix->read));
}
double fa_hitsounds_position(void *handle) {
    FAMix *mix = ((__bridge FAHitEngine *)handle)->mix;
    double start = atomic_load(&mix->lastStart);
    return start == 0 ? 0 : fmax(0, fmin(atomic_load(&mix->lastDuration), fa_audio_host_time() - start));
}
// Deterministic test entry point uses the exact render routine, before device muting.
void fa_hitsounds_render_test(void *handle, double time, unsigned frames, float *out) {
    render(((__bridge FAHitEngine *)handle)->mix, time, frames, out);
}

double fa_hitsounds_rendered_start(void *handle) { return atomic_load(&(((__bridge FAHitEngine *)handle)->mix->lastRenderedStart)); }
