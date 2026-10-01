using FruitsAtelier.App.Audio;
using System.Buffers.Binary;

internal static class Mp3TimelineTests
{
    public static void Run()
    {
        foreach (bool mono in new[] { false, true })
        foreach (string marker in new[] { "Info", "Xing" })
        foreach (uint flags in new uint[] { 0, 7, 15 })
        foreach (string encoder in new[] { "LAME", "Lavf", "Lavc" })
        foreach (int delay in new[] { 0, 576, 1105 })
        foreach (int padding in new[] { 0, 900 })
        {
            byte[] frame = new byte[512];
            int offset = mono ? 21 : 36;
            System.Text.Encoding.ASCII.GetBytes(marker).CopyTo(frame, offset);
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(offset + 4), flags);
            int tag = offset + 8 + ((flags & 1) != 0 ? 4 : 0) + ((flags & 2) != 0 ? 4 : 0)
                + ((flags & 4) != 0 ? 100 : 0) + ((flags & 8) != 0 ? 4 : 0);
            System.Text.Encoding.ASCII.GetBytes(encoder).CopyTo(frame, tag);
            frame[tag + 21] = (byte)(delay >> 4);
            frame[tag + 22] = (byte)(((delay & 15) << 4) | (padding >> 8));
            frame[tag + 23] = (byte)padding;
            int headerFrames = marker == "Info" ? 1152 : 0;
            if (Mp3Timeline.LeadingFrames(frame, mono, 1152) != headerFrames + (delay == 0 && padding == 0 ? -528 : delay + 1))
                throw new Exception("MP3 origin must follow the encoded delay for each channel/header layout");
        }
        if (Mp3Timeline.LeadingFrames(new byte[512], false, 1152) != -528)
            throw new Exception("Untagged MP3s must retain the legacy decoder delay");
    }
}
