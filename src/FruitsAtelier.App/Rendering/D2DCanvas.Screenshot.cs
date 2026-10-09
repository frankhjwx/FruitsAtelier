using System.Runtime.InteropServices;
using Vortice.Direct3D11;

namespace FruitsAtelier.App.Rendering;

public sealed partial class D2DCanvas
{
    internal (int Width, int Height, byte[] Pixels) CapturePixels()
    {
        if (drawing) throw new InvalidOperationException("Finish drawing before reading screenshot pixels.");
        // Flip-discard buffers must be read after EndDraw and before Present discards their contents.
        using var source = swapChain!.GetBuffer<ID3D11Texture2D>(0);
        var description = source.Description;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        description.MiscFlags = ResourceOptionFlags.None;
        using var staging = device!.CreateTexture2D(description);
        immediateContext!.CopyResource(staging, source);
        immediateContext.Map(staging, 0, MapMode.Read, MapFlags.None, out var mapped).CheckError();
        try
        {
            int stride = checked(width * 4);
            var pixels = new byte[checked(stride * height)];
            for (int row = 0; row < height; row++)
                Marshal.Copy(mapped.DataPointer + checked(row * (int)mapped.RowPitch), pixels, row * stride, stride);
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return (width, height, pixels);
        }
        finally { immediateContext.Unmap(staging, 0); }
    }
}
