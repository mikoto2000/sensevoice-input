using System.ComponentModel;
using System.Runtime.InteropServices;
using SenseVoiceInput.Core;
namespace SenseVoiceInput.Windows;
/// <summary>Only native transport and platform translation; routing returns suppression synchronously.</summary>
public sealed class GlobalKeyboardService(Func<GlobalKeyEvent, bool> route) : IDisposable
{
    private HookProc? callback;
    private nint hook;
    public void Start()
    {
        if (hook != 0) throw new InvalidOperationException("Keyboard hook already installed.");
        callback = OnHook;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private nint OnHook(int code, nint message, nint data)
    {
        if (code >= 0 && (int)message is 0x100 or 0x104 or 0x101 or 0x105)
        {
            int flags = Marshal.ReadInt32(data, 8);
            if ((flags & 0x10) == 0)
            {
                var key = WindowsKeyMap.Translate(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4), (flags & 1) != 0);
                var action = (int)message is 0x100 or 0x104 ? KeyAction.Down : KeyAction.Up;
                if (route(new(key, action, Environment.TickCount64))) return 1;
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }
    public void Dispose()
    {
        if (hook == 0) return;
        if (!UnhookWindowsHookEx(hook)) throw new Win32Exception(Marshal.GetLastWin32Error());
        hook = 0;
    }
    private delegate nint HookProc(int code, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(nint handle);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint handle, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
