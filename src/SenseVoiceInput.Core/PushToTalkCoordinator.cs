namespace SenseVoiceInput.Core;

public sealed class PushToTalkCoordinator(
    IAudioCaptureService audio,
    ISpeechRecognitionService recognizer,
    ITextInjectionService injector,
    IForegroundWindowService foreground) : IAsyncDisposable
{
    private readonly InputStateMachine machine = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private bool shuttingDown;
    private bool captureActive;
    private nint target;
    public InputState State => machine.State;
    public event Action<InputState> StateChanged { add => machine.Changed += value; remove => machine.Changed -= value; }
    public event Action<Exception>? Failed;
    public async Task KeyDownAsync()
    {
        if (shuttingDown || State != InputState.Idle) return;
        await gate.WaitAsync();
        try
        {
            if (shuttingDown || State != InputState.Idle) return;
            machine.MoveTo(InputState.Recording);
            target = foreground.GetForegroundWindow();
            captureActive = true;
            await audio.StartAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { await ResetAsync(); }
        catch (Exception e) { await HandleFailureAsync(e); }
        finally { gate.Release(); }
    }
    public async Task KeyUpAsync()
    {
        if (shuttingDown || State != InputState.Recording) return;
        await gate.WaitAsync();
        AudioData? data = null;
        try
        {
            if (shuttingDown || State != InputState.Recording) return;
            machine.MoveTo(InputState.Recognizing);
            data = await audio.StopAsync(lifetime.Token);
            captureActive = false;
            var result = await recognizer.RecognizeAsync(data, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                machine.MoveTo(InputState.Injecting);
                await injector.InjectAsync(result.Text, target, lifetime.Token);
            }
            machine.MoveTo(InputState.Idle);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { await ResetAsync(); }
        catch (Exception e) { await HandleFailureAsync(e); }
        finally { if (data != null) Array.Clear(data.Samples); gate.Release(); }
    }
    private async Task HandleFailureAsync(Exception error)
    {
        machine.MoveTo(InputState.Error);
        Failed?.Invoke(error);
        await ResetAsync();
    }
    private async Task ResetAsync()
    {
        if (captureActive)
        {
            try { var data = await audio.StopAsync(); Array.Clear(data.Samples); }
            catch (Exception e) { Failed?.Invoke(e); }
            finally { captureActive = false; }
        }
        if (State != InputState.Idle) machine.MoveTo(InputState.Idle);
    }
    public async ValueTask DisposeAsync()
    {
        shuttingDown = true;
        await lifetime.CancelAsync();
        await gate.WaitAsync();
        try { await ResetAsync(); }
        finally { gate.Release(); }
    }
}
