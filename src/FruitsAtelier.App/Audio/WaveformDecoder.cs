using FruitsAtelier.Core;
using NAudio.Wave;

namespace FruitsAtelier.App.Audio;

internal static class WaveformDecoder
{
    public static Task<AudioWaveform> Load(string path, CancellationToken token) => Task.Run(() =>
    {
        using WaveStream reader = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ogg" => new NAudio.Vorbis.VorbisWaveReader(path),
            ".wav" => new WaveFileReader(path),
            ".mp3" => MediaFoundationAudioReader.Open(path, () => token.IsCancellationRequested),
            _ => throw new NotSupportedException()
        };
        var source = reader.ToSampleProvider();
        return AudioWaveform.Read(source.WaveFormat.SampleRate, source.WaveFormat.Channels,
            b => source.Read(b, 0, b.Length), token);
    }, token);
}
