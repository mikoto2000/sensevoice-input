using System.Windows;
using SenseVoiceInput.Core;

namespace SenseVoiceInput.Windows;
/// <summary>Call from the WPF STA dispatcher. Clipboard contention is reported, never ignored.</summary>
public sealed class ClipboardDesktop(Func<int> restoreDelayMs) : IClipboardDesktop
{
    public bool IsTargetCurrent(nint target) => target != 0 && Win32.IsWindow(target) && Win32.GetForegroundWindow() == target;
    public uint Sequence => Win32.GetClipboardSequenceNumber();
    public IDisposable PreserveIme(nint target) => InputMethodControl.Preserve(target);
    public void DisableIme(nint target)
    {
        if (!IsTargetCurrent(target)) throw new InvalidOperationException("入力先が変わりました。");
        InputMethodControl.Disable(target);
    }
    public object? Snapshot()
    {
        var original = Clipboard.GetDataObject();
        if (original == null) return null;
        // Materialize formats before replacing their owner. If any format cannot be read, abort before modification.
        var snapshot = new DataObject();
        foreach (var format in original.GetFormats(false))
        {
            var value = original.GetData(format, false);
            if (value == null) throw new ClipboardSnapshotUnavailableException();
            if (value is MemoryStream stream) value = new MemoryStream(stream.ToArray());
            snapshot.SetData(format, value, false);
        }
        return snapshot;
    }
    public void SetText(string text)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        // Ask Windows clipboard history/cloud clipboard not to retain dictated text.
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
        Clipboard.SetDataObject(data, true);
    }
    public void Paste(nint target)
    {
        if (!IsTargetCurrent(target)) throw new InvalidOperationException("入力先が変わりました。");
        Win32.Paste();
    }
    public void TypeText(string text, nint target)
    {
        if (!IsTargetCurrent(target)) throw new InvalidOperationException("入力先が変わりました。");
        Win32.TypeText(text);
    }
    public Task SettleAsync() => Task.Delay(Math.Clamp(restoreDelayMs(), 500, 10000));
    public void Restore(object? snapshot)
    {
        if (snapshot == null) Clipboard.Clear(); else Clipboard.SetDataObject(snapshot, true);
    }
}
