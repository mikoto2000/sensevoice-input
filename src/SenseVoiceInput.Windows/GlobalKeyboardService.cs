using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using SenseVoiceInput.Core;

namespace SenseVoiceInput.Windows;
public interface IGlobalKeyboardService : IDisposable
{
    event Action<bool>? KeyChanged;
    void Start();
}
/// <summary>Hook runs on the WPF message loop. Queue work and immediately return to Windows.</summary>
public sealed class GlobalKeyboardService(Dispatcher dispatcher, int virtualKey = 0x14) : IGlobalKeyboardService
{
    private readonly PushToTalkKeyGate gate = new();
    private HookProc? callback;
    private nint hook;
    public event Action<bool>? KeyChanged;
    public void Start()
    {
        if (hook != 0) throw new InvalidOperationException("Keyboard hook already installed.");
        callback = OnHook;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private nint OnHook(int code, nint message, nint data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == virtualKey)
        {
            var kind = (int)message;
            if (kind is 0x100 or 0x104 or 0x101 or 0x105)
            {
                bool down = kind is 0x100 or 0x104;
                if (gate.Update(down)) dispatcher.BeginInvoke(() => KeyChanged?.Invoke(down), DispatcherPriority.Input);
                return 1; // Suppress both transitions, including auto-repeat: Caps Lock never toggles.
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
