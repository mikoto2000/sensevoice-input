using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SenseVoiceInput.Windows;

/// <summary>Observes physical make/break packets independently of IME virtual-key translation.</summary>
internal sealed class RawKeyboardMonitor(Action<int, int, int> received, Action<int> failed) : IDisposable
{
    private HwndSource? source;
    public void Start()
    {
        source = new HwndSource(new HwndSourceParameters("SenseVoiceInput.RawKeyboard") { ParentWindow = new nint(-3), Width = 0, Height = 0 });
        source.AddHook(OnMessage);
        if (!RegisterRawInputDevices([new() { UsagePage = 1, Usage = 6, Flags = 0x100, Target = source.Handle }], 1, (uint)Marshal.SizeOf<RawDevice>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0xFF) return 0;
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawHeader>();
        if (GetRawInputData(lParam, 0x10000003, 0, ref size, headerSize) == uint.MaxValue) { failed(Marshal.GetLastWin32Error()); return 0; }
        nint buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            if (GetRawInputData(lParam, 0x10000003, buffer, ref size, headerSize) == uint.MaxValue) { failed(Marshal.GetLastWin32Error()); return 0; }
            var header = Marshal.PtrToStructure<RawHeader>(buffer);
            if (header.Type == 1 && size >= headerSize + Marshal.SizeOf<RawKeyboard>())
            {
                var keyboard = Marshal.PtrToStructure<RawKeyboard>(buffer + (int)headerSize);
                if (keyboard.MakeCode == 0x3A) received(keyboard.VirtualKey, keyboard.MakeCode, keyboard.Flags);
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        // Let DefWindowProc perform foreground RAWINPUT cleanup.
        return 0;
    }
    public void Dispose()
    {
        if (source == null) return;
        bool removed = RegisterRawInputDevices([new() { UsagePage = 1, Usage = 6, Flags = 1 }], 1, (uint)Marshal.SizeOf<RawDevice>());
        int error = Marshal.GetLastWin32Error();
        source.Dispose(); source = null;
        if (!removed) throw new Win32Exception(error);
    }
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort UsagePage, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] private struct RawHeader { public uint Type, Size; public nint Device, WParam; }
    [StructLayout(LayoutKind.Sequential)] private struct RawKeyboard { public ushort MakeCode, Flags, Reserved, VirtualKey; public uint Message, Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
}
