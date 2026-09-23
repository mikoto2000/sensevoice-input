using System.Runtime.InteropServices;
using System.ComponentModel;
using SenseVoiceInput.Core;

namespace SenseVoiceInput.Windows;
internal static class Win32
{
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] input, int size);
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint Type; public INPUTUNION Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public MOUSEINPUT Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { public int X, Y; public uint MouseData, Flags, Time; public nuint Extra; }
    internal static void Paste()
    {
        if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0))
            throw new InvalidOperationException("修飾キーを離してから音声入力してください。");
        INPUT Key(ushort key, bool up) => new() { Type = 1, Data = new() { Keyboard = new() { Vk = key, Flags = up ? 2u : 0u } } };
        var inputs = new[] { Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true) };
        if (SendInput(4, inputs, Marshal.SizeOf<INPUT>()) != 4)
        {
            var error = Marshal.GetLastWin32Error();
            SendInput(2, [Key(0x56, true), Key(0x11, true)], Marshal.SizeOf<INPUT>());
            throw new Win32Exception(error, "SendInput failed. Check target integrity level / UIPI.");
        }
    }
}
public sealed class ForegroundWindowService : IForegroundWindowService
{
    public nint GetForegroundWindow() => Win32.GetForegroundWindow();
}
