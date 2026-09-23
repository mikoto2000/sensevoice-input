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
public sealed class GlobalKeyboardService(Dispatcher dispatcher, int virtualKey = 0x14, Action<int, int, int, int>? diagnostic = null) : IGlobalKeyboardService
{
    private PushToTalkHoldGate gate = new();
    private DispatcherTimer? releaseTimer;
    private RawKeyboardMonitor? rawMonitor;
    private readonly PushToTalkKeyBinding binding = new(virtualKey);
    private HookProc? callback;
    private nint hook;
    public event Action<bool>? KeyChanged;
    public void Start()
    {
        if (hook != 0) throw new InvalidOperationException("Keyboard hook already installed.");
        if (!SystemParametersInfo(0x16, 0, out uint delay, 0) || !SystemParametersInfo(0x0A, 0, out uint speed, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read keyboard repeat settings.");
        int repeatInterval = (int)Math.Ceiling(1000 / (2.5 + 27.5 * Math.Min(speed, 31) / 31));
        gate = new(250 * ((int)Math.Min(delay, 3) + 1), repeatInterval);
        if (diagnostic != null)
        {
            rawMonitor = new((vk, scan, flags) => diagnostic(0xFF, vk, scan, flags), error => diagnostic(0xFE, 0, 0, error));
            rawMonitor.Start();
        }
        callback = OnHook;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        releaseTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(10) };
        releaseTimer.Tick += OnReleaseTimer;
    }
    private nint OnHook(int code, nint message, nint data)
    {
        if (code >= 0 && diagnostic != null)
        {
            int vk = Marshal.ReadInt32(data), scan = Marshal.ReadInt32(data, 4), flags = Marshal.ReadInt32(data, 8), kind = (int)message;
            if (scan == 0x3A || vk is 0x14 or 0xF0 or 0xF1 or 0xF2)
                dispatcher.BeginInvoke(() => diagnostic(kind, vk, scan, flags));
        }
        // KBDLLHOOKSTRUCT: vkCode, scanCode, flags. JIS Eisu is often VK_OEM_ATTN (0xF0).
        // Match the physical Caps position before IME translation, on both down and up.
        if (code >= 0 && binding.Matches(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4), (Marshal.ReadInt32(data, 8) & 1) != 0))
        {
            var kind = (int)message;
            if (kind is 0x100 or 0x104 or 0x101 or 0x105)
            {
                bool down = kind is 0x100 or 0x104;
                bool jisModeKey = Marshal.ReadInt32(data) is >= 0xF0 and <= 0xF2;
                foreach (bool transition in gate.Update(down, Environment.TickCount64, jisModeKey)) QueueTransition(transition);
                if (gate.HasPendingRelease) releaseTimer?.Start(); else releaseTimer?.Stop();
                // Diagnostics must let OEM IME events through to observe raw delivery.
                if (diagnostic != null && Marshal.ReadInt32(data) is >= 0xF0 and <= 0xF2)
                    return CallNextHookEx(hook, code, message, data);
                return 1;
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }
    private void QueueTransition(bool down) => dispatcher.BeginInvoke(() =>
    {
        if (hook != 0) KeyChanged?.Invoke(down);
    }, DispatcherPriority.Input);
    private void OnReleaseTimer(object? sender, EventArgs e)
    {
        if (gate.FlushRelease(Environment.TickCount64)) QueueTransition(false);
        if (!gate.HasPendingRelease) releaseTimer?.Stop();
    }
    public void Dispose()
    {
        releaseTimer?.Stop();
        rawMonitor?.Dispose(); rawMonitor = null;
        if (hook == 0) return;
        if (!UnhookWindowsHookEx(hook)) throw new Win32Exception(Marshal.GetLastWin32Error());
        hook = 0;
    }
    private delegate nint HookProc(int code, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(nint handle);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint handle, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint parameter, out uint value, uint flags);
}
