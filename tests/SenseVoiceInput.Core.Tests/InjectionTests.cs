using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class InjectionTests
{
    private readonly FakeDesktop desktop = new();
    [Theory]
    [InlineData(TextInputMode.Unicode, true)] [InlineData(TextInputMode.Unicode, false)]
    [InlineData(TextInputMode.Clipboard, true)] [InlineData(TextInputMode.Clipboard, false)]
    public async Task RestoresOriginalImeOnlyAfterInputSettles(TextInputMode mode, bool open)
    {
        desktop.ImeOpen = open;
        desktop.SettleCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = new TextInjectionService(desktop, () => mode).InjectAsync("日本語", 42);
        Assert.False(task.IsCompleted);
        Assert.False(desktop.ImeOpen);
        Assert.DoesNotContain("ime-restore", desktop.Calls);
        desktop.SettleCompletion.SetResult();
        await task;
        Assert.Equal(open, desktop.ImeOpen);
        Assert.Equal("ime-restore", desktop.Calls[^1]);
    }
    [Theory] [InlineData(TextInputMode.Unicode)] [InlineData(TextInputMode.Clipboard)]
    public async Task InputFailureStillRestoresIme(TextInputMode mode)
    {
        desktop.FailDirect = desktop.FailPaste = true;
        await Assert.ThrowsAsync<IOException>(() => new TextInjectionService(desktop, () => mode).InjectAsync("text", 42));
        Assert.True(desktop.ImeOpen);
        Assert.Equal("ime-restore", desktop.Calls[^1]);
    }
    [Fact] public async Task CancellationAfterImeOffStillRestoresIme()
    {
        using var cancellation = new CancellationTokenSource();
        desktop.CancelOnIme = cancellation;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TextInjectionService(desktop, () => TextInputMode.Unicode).InjectAsync("text", 42, cancellation.Token));
        Assert.True(desktop.ImeOpen);
        Assert.Null(desktop.Pasted);
    }
    [Fact] public async Task EmptyResultDoesNotTouchIme()
    {
        await new TextInjectionService(desktop, () => TextInputMode.Unicode).InjectAsync(" ", 42);
        Assert.Empty(desktop.Calls);
    }
    [Theory] [InlineData(TextInputMode.Unicode)] [InlineData(TextInputMode.Clipboard)]
    public async Task ImeIsDisabledBeforeEitherInputMethod(TextInputMode mode)
    {
        await new TextInjectionService(desktop, () => mode).InjectAsync("日本語", 42);
        Assert.Equal("ime-save", desktop.Calls[0]);
        Assert.Equal("ime-off", desktop.Calls[1]);
        Assert.Equal("ime-restore", desktop.Calls[^1]);
        Assert.Equal("日本語", desktop.Pasted);
    }
    [Theory] [InlineData(TextInputMode.Unicode)] [InlineData(TextInputMode.Clipboard)]
    public async Task ImeFailurePreventsInputAndClipboardChanges(TextInputMode mode)
    {
        desktop.FailIme = true;
        await Assert.ThrowsAsync<IOException>(() => new TextInjectionService(desktop, () => mode).InjectAsync("text", 42));
        Assert.Equal(new[] { "ime-save", "ime-off", "ime-restore" }, desktop.Calls); Assert.Null(desktop.Pasted);
        Assert.Equal("before", desktop.Content);
    }
    [Fact] public async Task FocusChangeDuringImeOperationAbortsInput()
    {
        desktop.ChangeFocusOnIme = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TextInjectionService(desktop, () => TextInputMode.Unicode).InjectAsync("text", 42));
        Assert.Equal(new[] { "ime-save", "ime-off", "ime-restore" }, desktop.Calls); Assert.Null(desktop.Pasted);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task DirectInputNeverReadsOrChangesClipboard(bool unreadable)
    {
        desktop.Unreadable = unreadable;
        await new TextInjectionService(desktop, () => TextInputMode.Unicode).InjectAsync("日本語😀", 42);
        Assert.Equal("日本語😀", desktop.Pasted);
        Assert.Equal("before", desktop.Content);
        Assert.Equal(0u, desktop.Sequence);
        Assert.Equal(new[] { "ime-save", "ime-off", "direct", "settle", "ime-restore" }, desktop.Calls);
    }
    [Fact] public async Task DirectInputRejectsChangedTarget()
    {
        desktop.Foreground = 43;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TextInjectionService(desktop, () => TextInputMode.Unicode).InjectAsync("text", 42));
        Assert.Null(desktop.Pasted);
        Assert.Empty(desktop.Calls);
    }
    [Fact] public async Task DirectInputHonorsCancellation()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TextInjectionService(desktop, () => TextInputMode.Unicode).InjectAsync("text", 42, new CancellationToken(true)));
        Assert.Empty(desktop.Calls);
    }
    [Fact] public async Task ModeChangeAppliesToNextInput()
    {
        var mode = TextInputMode.Unicode;
        var sut = new TextInjectionService(desktop, () => mode);
        await sut.InjectAsync("direct", 42);
        mode = TextInputMode.Clipboard;
        await sut.InjectAsync("paste", 42);
        Assert.Equal(new[] { "ime-save", "ime-off", "direct", "settle", "ime-restore", "ime-save", "ime-off", "snapshot", "set", "paste", "settle", "restore", "ime-restore" }, desktop.Calls);
    }
    [Fact] public async Task ClipboardModeDoesNotSilentlySwitchMethods()
    {
        desktop.Unreadable = true;
        await Assert.ThrowsAsync<ClipboardSnapshotUnavailableException>(() => new TextInjectionService(desktop, () => TextInputMode.Clipboard).InjectAsync("text", 42));
        Assert.Equal(new[] { "ime-save", "ime-off", "snapshot", "ime-restore" }, desktop.Calls);
    }
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
        public bool ChangeDuringPaste, FailPaste, ChangeFocusOnSet, Unreadable, FailIme, ChangeFocusOnIme;
        public CancellationTokenSource? CancelOnPaste;
        public List<string> Calls = [];
        public bool ImeOpen = true;
        public TaskCompletionSource? SettleCompletion;
        public CancellationTokenSource? CancelOnIme;
        public bool FailDirect;
        public bool IsTargetCurrent(nint target) => target == Foreground;
        public IDisposable PreserveIme(nint target)
        {
            Calls.Add("ime-save");
            bool open = ImeOpen;
            return new SavedIme(() => { Calls.Add("ime-restore"); ImeOpen = open; });
        }
        private sealed class SavedIme(Action restore) : IDisposable { public void Dispose() => restore(); }
        public void DisableIme(nint target) { Calls.Add("ime-off"); ImeOpen = false; if (FailIme) throw new IOException(); if (ChangeFocusOnIme) Foreground = 43; CancelOnIme?.Cancel(); }
        public object Snapshot() { Calls.Add("snapshot"); if (Unreadable) throw new ClipboardSnapshotUnavailableException(); return Content; }
        public void TypeText(string text, nint target) { Calls.Add("direct"); if (FailDirect) throw new IOException(); Pasted = text; }
        public void SetText(string text) { Calls.Add("set"); Content = text; Sequence++; if (ChangeFocusOnSet) Foreground = 43; }
        public void Paste(nint target) { Calls.Add("paste"); if (FailPaste) throw new IOException(); Pasted = Content; CancelOnPaste?.Cancel(); }
        public Task SettleAsync() { Calls.Add("settle"); if (ChangeDuringPaste) { Content = "new copy"; Sequence++; } return SettleCompletion?.Task ?? Task.CompletedTask; }
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
