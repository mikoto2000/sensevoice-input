using System.ComponentModel;
using System.Runtime.InteropServices;
namespace SenseVoiceInput.Windows;
internal static class InputMethodControl
{
    internal static IDisposable Preserve(nint target)
    {
        nint focus = Focus(target);
        nint ime = ImmGetDefaultIMEWnd(focus);
        bool open = ime != 0 && Send(ime, 5) != 0;
        if (Focus(target) != focus) throw new InvalidOperationException("IME状態の保存中に入力欄が変わったため中止しました。");
        return new SavedState(target, focus, ime, open);
    }

    private sealed class SavedState(nint target, nint focus, nint ime, bool open) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (ime == 0 || !Win32.IsWindow(target) || !Win32.IsWindow(focus) || !Win32.IsWindow(ime)) return;
            // The default IME window is shared by controls on the target thread.
            // Never apply the old state to a different focused control or IME.
            if (ThreadFocus(target) != focus || ImmGetDefaultIMEWnd(focus) != ime) return;
            if ((Send(ime, 5) != 0) == open) return;
            Send(ime, 6, open ? 1 : 0);
            if ((Send(ime, 5) != 0) != open)
                throw new InvalidOperationException("入力先のIMEを元の状態に戻せませんでした。");
        }
    }

    // Control the target thread's default IME window; ImmSetOpenStatus on our
    // own thread's input context would not affect an editor in another process.
    internal static void Disable(nint target)
    {
        Win32.EnsureModifiersReleased();
        nint focus = Focus(target);
        nint ime = ImmGetDefaultIMEWnd(focus);
        if (ime == 0) return; // No IMM IME associated with this control.
        Send(ime, 6); // WM_IME_CONTROL / IMC_SETOPENSTATUS, lParam=FALSE
        if (Send(ime, 5) != 0) // IMC_GETOPENSTATUS
            throw new InvalidOperationException("入力先のIMEをオフにできませんでした。IMEを手動でオフにして再試行してください。");
        if (Focus(target) != focus) throw new InvalidOperationException("IME切替中に入力欄が変わったため中止しました。");
    }
    private static nint Focus(nint target)
    {
        if (Win32.GetForegroundWindow() != target) throw new InvalidOperationException("入力先が変わりました。");
        return ThreadFocus(target);
    }
    private static nint ThreadFocus(nint target)
    {
        uint thread = GetWindowThreadProcessId(target, out _);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (thread == 0 || !GetGUIThreadInfo(thread, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error(), "入力先のIME情報を取得できません。");
        if (info.Focus == 0) throw new InvalidOperationException("入力先にフォーカスがありません。");
        return info.Focus;
    }
    private static nuint Send(nint ime, nuint command, nint value = 0)
    {
        // Bound waits on foreign processes, and don't dispatch reentrant input while waiting.
        if (SendMessageTimeout(ime, 0x0283, command, value, 0x0003, 250, out var result) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "入力先のIMEが応答しません。IMEの状態を確認してください。");
        return result;
    }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("imm32.dll")] private static extern nint ImmGetDefaultIMEWnd(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)] private static extern nint SendMessageTimeout(nint hwnd, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
}
