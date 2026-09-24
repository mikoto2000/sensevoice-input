namespace SenseVoiceInput.Core;

public enum TextInputMode { Unicode, Clipboard }

public sealed class TextInjectionService(IClipboardDesktop desktop, Func<TextInputMode> mode) : ITextInjectionService
{
    private readonly ClipboardTextInjectionService clipboard = new(desktop);

    public async Task InjectAsync(string text, nint target, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        cancellationToken.ThrowIfCancellationRequested();
        var inputMode = mode();
        if (!Enum.IsDefined(inputMode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!desktop.IsTargetCurrent(target)) throw new InvalidOperationException("入力先が変わったため入力を中止しました。");
        using var ime = desktop.PreserveIme(target);
        desktop.DisableIme(target);
        cancellationToken.ThrowIfCancellationRequested();
        if (!desktop.IsTargetCurrent(target)) throw new InvalidOperationException("入力先が変わったため入力を中止しました。");
        switch (inputMode)
        {
            case TextInputMode.Clipboard:
                await clipboard.InjectAsync(text, target, cancellationToken);
                return;
            case TextInputMode.Unicode:
                if (!desktop.IsTargetCurrent(target)) throw new InvalidOperationException("入力先が変わったため入力を中止しました。");
                // SendInput queues events; keep IME off until the target has had
                // time to consume them, including after a partial-send failure.
                try { desktop.TypeText(text, target); }
                finally { await desktop.SettleAsync(); }
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
