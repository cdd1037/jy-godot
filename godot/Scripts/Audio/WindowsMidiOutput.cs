using System.Runtime.InteropServices;

namespace JinYong.Host;

/// <summary>Windows' installed MIDI synthesizer. No DOS decoding or game state lives here.</summary>
public sealed class WindowsMidiOutput : IDisposable
{
    private IntPtr _handle;

    public WindowsMidiOutput()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("当前音乐后端需要 Windows MIDI。");
        Check(midiOutOpen(out _handle, uint.MaxValue, 0, 0, 0), "open");
    }

    public void Send(int status, int data1, int data2)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        Check(midiOutShortMsg(_handle, (uint)(status | data1 << 8 | data2 << 16)), "send");
    }

    public void Reset()
    {
        if (_handle == IntPtr.Zero) return;
        Check(midiOutReset(_handle), "reset");
        for (var channel = 0; channel < 16; channel++)
        {
            Send(0xB0 | channel, 121, 0);
            Send(0xB0 | channel, 0, 0);
            Send(0xB0 | channel, 32, 0);
            Send(0xC0 | channel, 0, 0);
            Send(0xE0 | channel, 0, 64);
        }
    }

    public void Silence()
    {
        if (_handle != IntPtr.Zero) Check(midiOutReset(_handle), "silence");
    }

    public void Dispose()
    {
        var handle = _handle;
        _handle = IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        // Always release the device, even if resetting it reports a failure.
        _ = midiOutReset(handle);
        _ = midiOutClose(handle);
    }

    private static void Check(uint result, string operation)
    {
        if (result != 0) throw new InvalidOperationException($"Windows MIDI {operation} failed ({result}).");
    }

    // Microsoft Learn, mmeapi.h: CALLBACK_NULL; packed status/data bytes in low24 bits.
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint midiOutOpen(out IntPtr handle, uint device, nuint callback, nuint instance, uint flags);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint midiOutShortMsg(IntPtr handle, uint message);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint midiOutReset(IntPtr handle);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint midiOutClose(IntPtr handle);
}
