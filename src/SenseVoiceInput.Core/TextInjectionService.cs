namespace SenseVoiceInput.Core;

public enum TextInputMode { Unicode, Clipboard }

public sealed class TextInjectionService(IClipboardDesktop desktop, Func<TextInputMode> mode) : ITextInjectionService
{
    private readonly ClipboardTextInjectionService clipboard = new(desktop);

    public Task InjectAsync(string text, nint target, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        switch (mode())
        {
            case TextInputMode.Clipboard:
                return clipboard.InjectAsync(text, target, cancellationToken);
            case TextInputMode.Unicode:
                if (!desktop.IsTargetCurrent(target)) throw new InvalidOperationException("入力先が変わったため入力を中止しました。");
                desktop.TypeText(text, target);
                return Task.CompletedTask;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
