using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class AutoAndCaptureTests
{
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
