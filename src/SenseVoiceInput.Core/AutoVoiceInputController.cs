namespace SenseVoiceInput.Core;
public enum AutoVoiceState { Disabled, Ready, Armed, Listening, Processing }
/// <summary>A future adapter feeds SpeechDetected / SilenceDetectedAsync on the controller's dispatcher.
/// Start monitors speech; it must not run recognition or retain audio outside an armed session.</summary>
public interface IVoiceActivityDetector
{
    bool IsAvailable { get; }
    void Start(int silenceTimeoutMs);
    void Stop();
}
public sealed class UnavailableVoiceActivityDetector : IVoiceActivityDetector
{
    public bool IsAvailable => false;
    public void Start(int silenceTimeoutMs) { }
    public void Stop() { }
}
public sealed class AutoVoiceInputController(IVoiceActivityDetector vad, Func<AudioData, CancellationToken, Task> process) : IDisposable
{
    private bool on, textFocus, suspended, disposed, processing;
    private CancellationTokenSource session = new();
    private TaskCompletionSource? idle;
    public bool OnlyWhenTextInputFocused { get; set; } = true;
    public bool VadEnabled { get; set; } = true;
    public int SilenceTimeoutMs { get; set; } = 800;
    public bool IsOn => on;
    public bool IsProcessing => processing;
    public bool IsAvailable => vad.IsAvailable;
    public AutoVoiceState State { get; private set; } = AutoVoiceState.Disabled;
    public event Action<AutoVoiceState>? StateChanged;
    public event Action<Exception>? Failed;
    public void Toggle() { if (disposed) return; on = !on; ResetSession(); Refresh(); }
    public void TurnOff() { on = false; ResetSession(); Refresh(); }
    public void SetTextFocus(bool value) { if (textFocus == value) return; textFocus = value; ResetSession(); Refresh(); }
    public void SetSuspended(bool value) { if (suspended == value) return; suspended = value; ResetSession(); Refresh(); }
    private void SetState(AutoVoiceState state) { if (State == state) return; State = state; StateChanged?.Invoke(state); }
    private void ResetSession() { session.Cancel(); session.Dispose(); session = new(); vad.Stop(); }
    private void Refresh()
    {
        if (!on || disposed) { SetState(AutoVoiceState.Disabled); return; }
        if (suspended || processing || !VadEnabled || !vad.IsAvailable || OnlyWhenTextInputFocused && !textFocus) { SetState(AutoVoiceState.Ready); return; }
        try { vad.Start(SilenceTimeoutMs); SetState(AutoVoiceState.Armed); }
        catch (Exception e) { ReportFailure(e); }
    }
    public void ReportFailure(Exception error) { if (disposed) return; TurnOff(); Failed?.Invoke(error); }
    public Task StopAsync() { TurnOff(); return idle?.Task ?? Task.CompletedTask; }
    public void SpeechDetected() { if (State == AutoVoiceState.Armed) SetState(AutoVoiceState.Listening); }
    public async Task SilenceDetectedAsync(AudioData audio)
    {
        if (disposed || State != AutoVoiceState.Listening || processing) { Array.Clear(audio.Samples); return; }
        var token = session.Token;
        processing = true;
        var completion = idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            vad.Stop(); SetState(AutoVoiceState.Processing);
            await process(audio, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) { Failed?.Invoke(e); }
        finally
        {
            Array.Clear(audio.Samples); processing = false;
            try { if (!disposed) { Refresh(); StateChanged?.Invoke(State); } }
            finally { completion.TrySetResult(); }
        }
    }
    public void Dispose() { if (disposed) return; disposed = true; on = false; session.Cancel(); session.Dispose(); vad.Stop(); SetState(AutoVoiceState.Disabled); }
}
