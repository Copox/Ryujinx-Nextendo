using Ryujinx.HLE.HOS.Applets.SoftwareKeyboard;
using Ryujinx.HLE.HOS.Services.SurfaceFlinger;
using Ryujinx.HLE.UI;
using Ryujinx.Memory;
using System;

// Execute the production Skia renderer, including its guest write. Only the
// destination interface is reduced to the Write member exercised by this class.
namespace Ryujinx.Memory
{
    public interface IVirtualMemoryManager
    {
        void Write(ulong address, ReadOnlySpan<byte> data);
    }
}

namespace Ryujinx.HLE.HOS.Services.SurfaceFlinger
{
    public enum ColorFormat { A8B8G8R8 }
}

sealed class GuardedMemory : IVirtualMemoryManager
{
    public byte[] Bytes { get; }
    public int LastWriteLength { get; private set; }
    public GuardedMemory(int length)
    {
        Bytes = new byte[length];
        Array.Fill(Bytes, (byte)0xa5);
    }
    public void Write(ulong address, ReadOnlySpan<byte> data)
    {
        data.CopyTo(Bytes.AsSpan(checked((int)address), data.Length));
        LastWriteLength = data.Length;
    }
}

sealed class TestTheme : IHostUITheme
{
    public string FontFamily => "Arial";
    public ThemeColor DefaultBackgroundColor => new(1, 0.1f, 0.1f, 0.1f);
    public ThemeColor DefaultForegroundColor => new(1, 1, 1, 1);
    public ThemeColor DefaultBorderColor => new(1, 0.4f, 0.4f, 0.4f);
    public ThemeColor SelectionBackgroundColor => new(1, 0.2f, 0.2f, 0.5f);
    public ThemeColor SelectionForegroundColor => new(1, 1, 1, 1);
}

static class Program
{
    static void Main()
    {
        Verify(1280, 720, 5120, 0x3a0000, 0x38b818);
        Verify(640, 360, 2560, 0x100000, 0xe2000);
        Verify(319, 281, 1280, 0x60000, 0x58000);
        Console.WriteLine("PASS: keyboard pixels copied; row pitch preserved; allocation tail and controller guard intact.");
    }

    static void Verify(uint width, uint height, uint pitch, uint capacity, int controllerOffset)
    {
        const int Position = 4096;
        var memory = new GuardedMemory(Position + checked((int)capacity) + 4096);
        var renderer = new SoftwareKeyboardRendererBase(new TestTheme());
        if (renderer.WriteBufferToMemory(memory, Position))
        {
            throw new Exception("Unrendered keyboard reported a frame");
        }
        renderer.CreateSurface(new RenderingSurfaceInfo(ColorFormat.A8B8G8R8, width, height, pitch, capacity));
        renderer.DrawMutableElements(new SoftwareKeyboardUIState { InputText = "112" });
        renderer.CopyImageToBuffer();
        int pixelBytes = checked((int)(pitch * height));
        for (int frame = 0; frame < 2; frame++)
        {
            if (!renderer.WriteBufferToMemory(memory, Position) || memory.LastWriteLength != pixelBytes)
            {
                throw new Exception($"Incorrect image write for {width}x{height}: {memory.LastWriteLength}, expected {pixelBytes}");
            }
            foreach (byte guard in memory.Bytes.AsSpan(Position + pixelBytes))
            {
                if (guard != 0xa5) throw new Exception("Guest allocation tail overwritten");
            }
            foreach (byte guard in memory.Bytes.AsSpan(0, Position))
            {
                if (guard != 0xa5) throw new Exception("Guest prefix overwritten");
            }
            if (memory.Bytes[Position + controllerOffset] != 0xa5)
            {
                throw new Exception("SM3DW controller overwritten");
            }
        }
        Console.WriteLine($"PASS: {width}x{height}, pitch={pitch}, capacity=0x{capacity:x}, copied=0x{pixelBytes:x}");
    }
}
