using System.Runtime.InteropServices;
using SoundTouch;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Mac;

public sealed partial class MacAudio
{
    private readonly Timer bufferTimer;
    private MusicBuffers? buffers;

    private void PumpBuffers()
    {
        lock (gate)
        {
            if (disposed || player == 0 || !playbackRequested || Position(player) >= Duration(player)) return;
            try { buffers?.Fill(); }
            catch (Exception exception)
            {
                PauseNative(player); playbackRequested = false;
                error = L.Get("files.failed", exception.Message);
                MacPaths.Log(exception.ToString());
            }
        }
    }

    // Same pinned SoundTouch processor as the Windows music transport.
    // Decode/stretch work runs before playback or on the producer timer, never
    // in the native audio callback. A bounded half-second queue absorbs UI stalls.
    private sealed class MusicBuffers
    {
        private readonly nint player;
        private readonly int channels;
        private readonly float[] input, output;
        private readonly SoundTouchProcessor? processor;
        private bool ended, drained;

        internal MusicBuffers(nint player, double speed, bool shiftPitch)
        {
            this.player = player;
            channels = Channels(player);
            input = new float[2048 * channels]; output = new float[input.Length];
            if (speed == 1) return;
            processor = new() { SampleRate = SampleRate(player), Channels = channels,
                Tempo = shiftPitch ? 1 : speed, Rate = shiftPitch ? speed : 1 };
            if (speed <= .25)
            {
                processor.SetSetting(SettingId.UseQuickSeek, 1);
                processor.SetSetting(SettingId.SequenceDurationMs, 30);
                processor.SetSetting(SettingId.OverlapDurationMs, 4);
            }
        }

        internal void Fill()
        {
            while (!drained && Buffered(player) < .5)
            {
                int frames = Read();
                if (frames == 0) { drained = true; break; }
                Enqueue(player, output, frames);
            }
        }

        private int Read()
        {
            if (processor is null) return ReadSource(output);
            int written = 0;
            while (written < 2048)
            {
                int frames = processor.ReceiveSamples(output.AsSpan(written * channels), 2048 - written);
                written += frames;
                if (frames > 0) continue;
                if (ended) break;
                int read = ReadSource(input);
                if (read == 0) { processor.Flush(); ended = true; }
                else processor.PutSamples(input.AsSpan(0, read * channels), read);
            }
            return written;
        }

        private int ReadSource(float[] destination)
        {
            int frames = ReadNative(player, destination, 2048);
            if (frames < 0) throw new IOException(L.Get("mac.audioFormat"));
            return frames;
        }
    }

    internal void MonitorPcm() { lock (gate) if (player != 0) MonitorNative(player); }
    internal double RenderedOnset { get { lock (gate) return player == 0 ? 0 : RenderedOnsetNative(player); } }
    internal double RenderedFrequency { get { lock (gate) return player == 0 ? 0 : RenderedFrequencyNative(player); } }
    [DllImport(Library, EntryPoint = "fa_audio_channels")] private static extern int Channels(nint player);
    [DllImport(Library, EntryPoint = "fa_audio_sample_rate")] private static extern int SampleRate(nint player);
    [DllImport(Library, EntryPoint = "fa_audio_read")] private static extern int ReadNative(nint player, [Out] float[] output, int frames);
    [DllImport(Library, EntryPoint = "fa_audio_enqueue")] private static extern void Enqueue(nint player, float[] input, int frames);
    [DllImport(Library, EntryPoint = "fa_audio_buffered")] private static extern double Buffered(nint player);
    [DllImport(Library, EntryPoint = "fa_audio_monitor")] private static extern void MonitorNative(nint player);
    [DllImport(Library, EntryPoint = "fa_audio_rendered_onset")] private static extern double RenderedOnsetNative(nint player);
    [DllImport(Library, EntryPoint = "fa_audio_rendered_frequency")] private static extern double RenderedFrequencyNative(nint player);
}
