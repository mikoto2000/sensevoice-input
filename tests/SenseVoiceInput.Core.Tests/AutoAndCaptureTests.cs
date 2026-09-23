using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class AutoAndCaptureTests
{
    [Fact] public void VadStartFailureTurnsAutoOffAndReports()
    {
        using var c = new AutoVoiceInputController(new FailingVad(), (_, _) => Task.CompletedTask);
        Exception? error = null; c.Failed += e => error = e;
        c.SetTextFocus(true); c.Toggle();
        Assert.False(c.IsOn); Assert.Equal(AutoVoiceState.Disabled, c.State); Assert.NotNull(error);
    }
    [Fact] public async Task ShutdownWaitsForPendingProcessingAndCancelsIt()
    {
        var wait = new TaskCompletionSource(); CancellationToken token = default;
        using var c = new AutoVoiceInputController(new FakeVad(), async (_, ct) => { token = ct; await wait.Task; });
        c.SetTextFocus(true); c.Toggle(); c.SpeechDetected();
        var processing = c.SilenceDetectedAsync(new([1f], 16000));
        var shutdown = c.StopAsync(); Assert.False(shutdown.IsCompleted); Assert.True(token.IsCancellationRequested);
        wait.SetResult(); await shutdown; await processing; Assert.Equal(AutoVoiceState.Disabled, c.State);
    }
    [Fact] public async Task CancellationCompletionNotifiesThatSettingsCanBeEditedAgain()
    {
        var wait = new TaskCompletionSource(); bool idleNotified = false;
        using var c = new AutoVoiceInputController(new FakeVad(), (_, _) => wait.Task);
        c.SetTextFocus(true); c.Toggle(); c.SpeechDetected();
        var processing = c.SilenceDetectedAsync(new([1f], 16000));
        c.TurnOff(); c.StateChanged += _ => { if (!c.IsProcessing) idleNotified = true; };
        wait.SetResult(); await processing; Assert.True(idleNotified);
    }
    private sealed class FailingVad : IVoiceActivityDetector
    {
        public bool IsAvailable => true;
        public void Start(int silenceTimeoutMs) => throw new IOException("device gone");
        public void Stop() { }
    }
    [Fact] public async Task DuplicateSilenceDoesNotRearmDuringProcessing()
    {
        var wait = new TaskCompletionSource();
        using var c = new AutoVoiceInputController(new FakeVad(), (_, _) => wait.Task);
        c.Toggle(); c.SetTextFocus(true); c.SpeechDetected();
        var work = c.SilenceDetectedAsync(new([1f], 16000));
        await c.SilenceDetectedAsync(new([2f], 16000));
        Assert.Equal(AutoVoiceState.Processing, c.State);
        wait.SetResult(); await work;
    }
    [Fact] public async Task AutoStateFlowAndToggleOff()
    {
        var vad = new FakeVad(); var processed = 0;
        using var c = new AutoVoiceInputController(vad, (_, _) => { processed++; return Task.CompletedTask; });
        c.Toggle(); Assert.Equal(AutoVoiceState.Ready, c.State);
        c.SetTextFocus(true); Assert.Equal(AutoVoiceState.Armed, c.State); Assert.True(vad.Running);
        c.SpeechDetected(); Assert.Equal(AutoVoiceState.Listening, c.State);
        await c.SilenceDetectedAsync(new([1f], 16000));
        Assert.Equal(1, processed); Assert.Equal(AutoVoiceState.Armed, c.State);
        c.Toggle(); Assert.Equal(AutoVoiceState.Disabled, c.State); Assert.False(vad.Running);
    }
    [Fact] public async Task OffDuringProcessingCancelsAndDoesNotRearm()
    {
        var wait = new TaskCompletionSource(); CancellationToken token = default;
        using var c = new AutoVoiceInputController(new FakeVad(), async (_, ct) => { token = ct; await wait.Task; });
        c.SetTextFocus(true); c.Toggle(); c.SpeechDetected();
        var work = c.SilenceDetectedAsync(new([1f], 16000));
        Assert.Equal(AutoVoiceState.Processing, c.State); c.Toggle(); Assert.True(token.IsCancellationRequested);
        wait.SetResult(); await work; Assert.Equal(AutoVoiceState.Disabled, c.State);
    }
    [Fact] public void FocusLossAndPttSuspendDisarmVad()
    {
        var vad = new FakeVad(); using var c = new AutoVoiceInputController(vad, (_, _) => Task.CompletedTask);
        c.Toggle(); c.SetTextFocus(true); c.SpeechDetected(); c.SetTextFocus(false);
        Assert.Equal(AutoVoiceState.Ready, c.State); Assert.False(vad.Running);
        c.SetTextFocus(true); c.SetSuspended(true); Assert.Equal(AutoVoiceState.Ready, c.State);
        c.SetSuspended(false); Assert.Equal(AutoVoiceState.Armed, c.State);
    }
    [Fact] public void CaptureChordAndEscape()
    {
        var c = new TriggerCaptureSession(TriggerType.KEY_COMBINATION);
        c.OnKeyEvent(new(KeyCode.LEFT_CTRL, KeyAction.Down, 0)); c.OnKeyEvent(new(KeyCode.GRAVE, KeyAction.Down, 1));
        c.OnKeyEvent(new(KeyCode.GRAVE, KeyAction.Up, 2)); Assert.Null(c.Candidate);
        c.OnKeyEvent(new(KeyCode.LEFT_CTRL, KeyAction.Up, 3)); Assert.Equal(new[] { KeyCode.LEFT_CTRL, KeyCode.GRAVE }, c.Candidate!.Keys);
        var cancel = new TriggerCaptureSession(TriggerType.SINGLE_KEY); cancel.OnKeyEvent(new(KeyCode.ESCAPE, KeyAction.Down, 0)); Assert.True(cancel.Cancelled);
    }
    private sealed class FakeVad : IVoiceActivityDetector
    {
        public bool IsAvailable => true;
        public bool Running;
        public void Start(int silenceTimeoutMs) => Running = true;
        public void Stop() => Running = false;
    }
}
