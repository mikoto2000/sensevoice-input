using SenseVoiceInput.Core;

namespace SenseVoiceInput.Core.Tests;

public sealed class AutoVoiceInputProcessorTests
{
    [Fact]
    public async Task StaleFocusFromAnotherWindowDoesNotStartRecognition()
    {
        var desktop = new Desktop();
        var sut = new AutoVoiceInputProcessor(desktop, desktop, desktop,
            _ => Task.FromResult<TextInputFocus?>(new(41, "field")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ProcessAsync(new([], 16000), 42, "field", default));

        Assert.Equal(0, desktop.Recognitions);
        Assert.Null(desktop.Injected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangedWindowOrFieldAfterRecognitionPreventsInjection(bool changeWindow)
    {
        var desktop = new Desktop();
        string field = "original";
        desktop.DuringRecognition = () =>
        {
            if (changeWindow) desktop.Foreground = 43;
            else field = "changed";
        };
        var sut = new AutoVoiceInputProcessor(desktop, desktop, desktop,
            _ => Task.FromResult<TextInputFocus?>(new(desktop.Foreground, field)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ProcessAsync(new([], 16000), 42, "original", default));

        Assert.Equal(1, desktop.Recognitions);
        Assert.Null(desktop.Injected);
    }

    [Fact]
    public async Task WindowOnlyModeDoesNotRequireAnAutomationProvider()
    {
        var desktop = new Desktop();
        var sut = new AutoVoiceInputProcessor(desktop, desktop, desktop, _ => throw new NotSupportedException());

        await sut.ProcessAsync(new([], 16000), 42, null, default);

        Assert.Equal("recognized", desktop.Injected);
    }

    private sealed class Desktop : IForegroundWindowService, ISpeechRecognitionService, ITextInjectionService
    {
        public nint Foreground = 42;
        public int Recognitions;
        public string? Injected;
        public Action? DuringRecognition;
        public nint GetForegroundWindow() => Foreground;

        public Task<SpeechRecognitionResult> RecognizeAsync(AudioData audio, CancellationToken cancellationToken)
        {
            Recognitions++;
            DuringRecognition?.Invoke();
            return Task.FromResult(new SpeechRecognitionResult("recognized", "ja"));
        }

        public Task InjectAsync(string text, nint target, CancellationToken cancellationToken)
        {
            Injected = text;
            return Task.CompletedTask;
        }
    }
}
