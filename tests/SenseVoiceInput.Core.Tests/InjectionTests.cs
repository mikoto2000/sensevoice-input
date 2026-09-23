using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class InjectionTests
{
    private readonly FakeDesktop desktop = new();
    [Fact] public async Task PasteRestoresClipboardAfterConsumerCompletes()
    {
        var sut = new ClipboardTextInjectionService(desktop);
        await sut.InjectAsync("日本語", 42);
        Assert.Equal("日本語", desktop.Pasted);
        Assert.Equal("before", desktop.Content);
        Assert.Equal(new[] { "snapshot", "set", "paste", "settle", "restore" }, desktop.Calls);
    }
    [Fact] public async Task FocusChangeDoesNotTouchClipboard()
    {
        desktop.Foreground = 43;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ClipboardTextInjectionService(desktop).InjectAsync("text", 42));
        Assert.Empty(desktop.Calls);
    }
    [Fact] public async Task NewClipboardDataIsNotOverwritten()
    {
        desktop.ChangeDuringPaste = true;
        await new ClipboardTextInjectionService(desktop).InjectAsync("text", 42);
        Assert.Equal("new copy", desktop.Content);
    }
    [Fact] public async Task PasteFailureStillRestores()
    {
        desktop.FailPaste = true;
        await Assert.ThrowsAsync<IOException>(() => new ClipboardTextInjectionService(desktop).InjectAsync("text", 42));
        Assert.Equal("before", desktop.Content);
    }
    private sealed class FakeDesktop : IClipboardDesktop
    {
        public nint Foreground = 42;
        public string Content = "before";
        public string? Pasted;
        public uint Sequence { get; private set; }
        public bool ChangeDuringPaste, FailPaste, ChangeFocusOnSet;
        public CancellationTokenSource? CancelOnPaste;
        public List<string> Calls = [];
        public bool IsTargetCurrent(nint target) => target == Foreground;
        public object Snapshot() { Calls.Add("snapshot"); return Content; }
        public void SetText(string text) { Calls.Add("set"); Content = text; Sequence++; if (ChangeFocusOnSet) Foreground = 43; }
        public void Paste(nint target) { Calls.Add("paste"); if (FailPaste) throw new IOException(); Pasted = Content; CancelOnPaste?.Cancel(); }
        public Task SettleAsync() { Calls.Add("settle"); if (ChangeDuringPaste) { Content = "new copy"; Sequence++; } return Task.CompletedTask; }
        public void Restore(object? snapshot) { Calls.Add("restore"); Content = (string)snapshot!; Sequence++; }
    }
    [Fact] public async Task FocusChangeAfterClipboardSetAbortsAndRestores()
    {
        desktop.ChangeFocusOnSet = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ClipboardTextInjectionService(desktop).InjectAsync("text", 42));
        Assert.Null(desktop.Pasted); Assert.Equal("before", desktop.Content);
    }
    [Fact] public async Task CancellationAfterPasteStillWaitsAndRestores()
    {
        using var ct = new CancellationTokenSource(); desktop.CancelOnPaste = ct;
        await new ClipboardTextInjectionService(desktop).InjectAsync("text", 42, ct.Token);
        Assert.Contains("settle", desktop.Calls); Assert.Equal("before", desktop.Content);
    }
    [Fact] public async Task AlreadyCancelledDoesNotTouchClipboard()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ClipboardTextInjectionService(desktop).InjectAsync("text", 42, new CancellationToken(true)));
        Assert.Empty(desktop.Calls);
    }
}
