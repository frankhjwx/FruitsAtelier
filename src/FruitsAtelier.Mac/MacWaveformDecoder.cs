using System.Runtime.InteropServices;
using FruitsAtelier.Core;

namespace FruitsAtelier.Mac;

internal static class MacWaveformDecoder
{
    public static Task<AudioWaveform> Load(string path, CancellationToken token) => Task.Run(() =>
    {
        if (Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new NVorbis.VorbisReader(path);
            return AudioWaveform.Read(reader.SampleRate, reader.Channels, b => reader.ReadSamples(b, 0, b.Length), token);
        }
        var handle = Open(path, out int rate, out int channels);
        if (handle == 0) throw new InvalidDataException("Could not decode waveform audio.");
        try
        {
            return AudioWaveform.Read(rate, channels, b =>
            {
                int count = Read(handle, b, b.Length);
                return count >= 0 ? count : throw new InvalidDataException("Could not read waveform audio.");
            }, token);
        }
        finally { Close(handle); }
    }, token);

    [DllImport("FruitsAtelierAudio", EntryPoint = "fa_waveform_open")]
    private static extern nint Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out int rate, out int channels);
    [DllImport("FruitsAtelierAudio", EntryPoint = "fa_waveform_read")]
    private static extern int Read(nint handle, [Out] float[] samples, int count);
    [DllImport("FruitsAtelierAudio", EntryPoint = "fa_waveform_close")]
    private static extern void Close(nint handle);
}
