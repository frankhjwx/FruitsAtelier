using System.Buffers.Binary;
using NAudio.Wave;

namespace FruitsAtelier.App.Audio;

internal static class Mp3Timeline
{
    // Media Foundation omits 528 decoder-delay frames retained by legacy BASS for untagged MP3s.
    // Tagged streams also include the Xing frame in MF output; their encoder delay is explicit.
    internal static int LeadingFrames(string path)
    {
        using var input = File.OpenRead(path);
        Span<byte> header = stackalloc byte[10];
        if (input.Read(header) == 10 && header[..3].SequenceEqual("ID3"u8))
        {
            if ((header[6] | header[7] | header[8] | header[9]) >= 128) return 0;
            input.Position = 10L + (header[6] << 21 | header[7] << 14 | header[8] << 7 | header[9]);
        }
        else input.Position = 0;
        var frame = Mp3Frame.LoadFromStream(input);
        if (frame is null || frame.SampleCount != 1152) return 0;
        return LeadingFrames(frame.RawData, frame.ChannelMode == ChannelMode.Mono, frame.SampleCount);
    }

    internal static int LeadingFrames(byte[] frame, bool mono, int samplesPerFrame)
    {
        int offset = mono ? 21 : 36;
        if (frame.Length < offset + 8) return -528;
        var marker = frame.AsSpan(offset, 4);
        if (!marker.SequenceEqual("Xing"u8) && !marker.SequenceEqual("Info"u8)) return -528;
        uint flags = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(offset + 4, 4));
        int tag = offset + 8 + ((flags & 1) != 0 ? 4 : 0) + ((flags & 2) != 0 ? 4 : 0)
            + ((flags & 4) != 0 ? 100 : 0) + ((flags & 8) != 0 ? 4 : 0);
        if (tag + 24 > frame.Length) return samplesPerFrame - 528;
        var encoder = frame.AsSpan(tag, 4);
        if (!encoder.SequenceEqual("LAME"u8) && !encoder.SequenceEqual("Lavf"u8) && !encoder.SequenceEqual("Lavc"u8))
            return samplesPerFrame - 528;
        int delay = frame[tag + 21] << 4 | frame[tag + 22] >> 4;
        // Some encoders write a LAME identifier but leave gapless metadata empty.
        // BASS retains decoder delay in that case, just as for an untagged Xing stream.
        if (delay == 0) return samplesPerFrame - 528;
        return samplesPerFrame + delay + 1;
    }
}
