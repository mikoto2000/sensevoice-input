namespace SenseVoiceInput.Core;

public sealed record TextInputFocus(nint Window, string Identity);

/// <summary>Recognizes an automatic recording and checks its original destination before sending text.</summary>
public sealed class AutoVoiceInputProcessor(
    ISpeechRecognitionService recognizer,
    ITextInjectionService injector,
    IForegroundWindowService foreground,
    Func<CancellationToken, Task<TextInputFocus?>> readFocus)
{
    public async Task ProcessAsync(AudioData audio, nint target, string? requiredField, CancellationToken cancellationToken)
    {
        await CheckTargetAsync(target, requiredField, cancellationToken);
        var result = await recognizer.RecognizeAsync(audio, cancellationToken);
        await CheckTargetAsync(target, requiredField, cancellationToken);
        if (!string.IsNullOrWhiteSpace(result.Text))
            await injector.InjectAsync(result.Text, target, cancellationToken);
    }

    private async Task CheckTargetAsync(nint target, string? requiredField, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (target != 0 && foreground.GetForegroundWindow() == target)
        {
            if (requiredField == null) return;
            var focus = await readFocus(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (focus == new TextInputFocus(target, requiredField) && foreground.GetForegroundWindow() == target) return;
        }
        throw new InvalidOperationException("入力先が変わったため自動入力を中止しました。");
    }
}
