namespace SenseVoiceInput.Core;
public sealed class ClipboardSnapshotUnavailableException(Exception? inner = null)
    : Exception("クリップボードの全形式を退避できません。", inner);
public interface IClipboardDesktop
{
    bool IsTargetCurrent(nint target);
    IDisposable PreserveIme(nint target);
    void DisableIme(nint target);
    uint Sequence { get; }
    object? Snapshot();
    void TypeText(string text, nint target);
    void SetText(string text);
    void Paste(nint target);
    Task SettleAsync();
    void Restore(object? snapshot);
}
public sealed class ClipboardTextInjectionService(IClipboardDesktop desktop) : ITextInjectionService
{
    public async Task InjectAsync(string text, nint target, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        cancellationToken.ThrowIfCancellationRequested();
        if (!desktop.IsTargetCurrent(target)) throw new InvalidOperationException("入力先が変わったため貼り付けを中止しました。");
        var snapshot = desktop.Snapshot();
        desktop.SetText(text);
        var sequence = desktop.Sequence;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!desktop.IsTargetCurrent(target)) throw new InvalidOperationException("入力先が変わったため貼り付けを中止しました。");
            // Even a partial SendInput failure can leave queued paste events.
            // Restoration is deliberately non-cancellable, including on shutdown.
            try { desktop.Paste(target); }
            finally { await desktop.SettleAsync(); }
        }
        finally { if (desktop.Sequence == sequence) desktop.Restore(snapshot); }
    }
}
