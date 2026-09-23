using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;

public class CoordinatorTests
{
    private readonly FakeAudio audio = new();
    private readonly FakeRecognizer recognizer = new();
    private readonly FakeInjector injector = new();
    private readonly FakeForeground foreground = new();
    private PushToTalkCoordinator Create() => new(audio, recognizer, injector, foreground);

    [Fact] public async Task DownStartsCaptureOnlyOnce()
    {
        await using var sut = Create();
        await sut.KeyDownAsync();
        await sut.KeyDownAsync();
        Assert.Equal(1, audio.Starts);
        Assert.Equal(InputState.Recording, sut.State);
    }

    [Fact] public async Task UpRecognizesAndInjectsIntoOriginalTarget()
    {
        await using var sut = Create();
        var states = new List<InputState>();
        sut.StateChanged += states.Add;
        await sut.KeyDownAsync();
        await sut.KeyUpAsync();
        Assert.Equal(1, audio.Stops);
        Assert.Same(audio.Data, recognizer.Received);
        Assert.Equal("日本語", injector.Text);
        Assert.Equal((nint)42, injector.Target);
        Assert.Equal(new[] { InputState.Recording, InputState.Recognizing, InputState.Injecting, InputState.Idle }, states);
    }
    [Fact] public async Task EmptyRecognitionDoesNotInject()
    {
        recognizer.Text = "  ";
        await using var sut = Create();
        await sut.KeyDownAsync(); await sut.KeyUpAsync();
        Assert.Null(injector.Text);
        Assert.Equal(InputState.Idle, sut.State);
    }
    private sealed class FakeAudio : IAudioCaptureService
    {
        public int Starts;
        public int Stops;
        public AudioData Data = new([0.1f], 16000);
        public bool FailStart;
        public bool FailStop;
        public TaskCompletionSource? PendingStart;
        public async Task StartAsync(CancellationToken ct) { Starts++; if (FailStart) throw new IOException("device"); if (PendingStart != null) await PendingStart.Task.WaitAsync(ct); }
        public Task<AudioData> StopAsync(CancellationToken ct) { Stops++; if (FailStop) throw new IOException("stop"); return Task.FromResult(Data); }
    }
    private sealed class FakeRecognizer : ISpeechRecognitionService
    {
        public string Text = "日本語";
        public AudioData? Received;
        public bool Fail;
        public TaskCompletionSource? Pending;
        public async Task<SpeechRecognitionResult> RecognizeAsync(AudioData audio, CancellationToken ct) { Received = audio; if (Fail) throw new IOException("model"); if (Pending != null) await Pending.Task.WaitAsync(ct); return new SpeechRecognitionResult(Text, "ja"); }
    }
    private sealed class FakeInjector : ITextInjectionService
    {
        public string? Text;
        public nint Target;
        public bool Fail;
        public Task InjectAsync(string text, nint target, CancellationToken ct) { if (Fail) throw new IOException("paste"); Text = text; Target = target; return Task.CompletedTask; }
    }
    private sealed class FakeForeground : IForegroundWindowService { public nint GetForegroundWindow() => 42; }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task FailureIsReportedAndReturnsIdle(bool startFailure)
    {
        audio.FailStart = startFailure; recognizer.Fail = !startFailure;
        await using var sut = Create();
        var states = new List<InputState>(); Exception? error = null;
        sut.StateChanged += states.Add; sut.Failed += e => error = e;
        await sut.KeyDownAsync(); await sut.KeyUpAsync();
        Assert.IsType<IOException>(error);
        Assert.Contains(InputState.Error, states);
        Assert.Equal(InputState.Idle, sut.State);
        Assert.Null(injector.Text);
    }
    [Fact] public async Task DownDuringRecognitionIsIgnoredAndShutdownCancels()
    {
        recognizer.Pending = new();
        var sut = Create();
        await sut.KeyDownAsync();
        var pending = sut.KeyUpAsync();
        Assert.Equal(InputState.Recognizing, sut.State);
        await sut.KeyDownAsync();
        Assert.Equal(1, audio.Starts);
        await sut.DisposeAsync(); await pending;
        Assert.Equal(InputState.Idle, sut.State);
        Assert.Null(injector.Text);
        await sut.KeyDownAsync();
        Assert.Equal(1, audio.Starts);
    }
    [Fact] public async Task ShutdownWhileRecordingStopsCapture()
    {
        var sut = Create(); await sut.KeyDownAsync(); await sut.DisposeAsync();
        Assert.Equal(1, audio.Stops);
        Assert.Equal(InputState.Idle, sut.State);
    }
    [Fact] public async Task UpWhileIdleIsIgnored()
    {
        await using var sut = Create(); await sut.KeyUpAsync(); Assert.Equal(0, audio.Stops);
    }
    [Fact] public async Task ReleaseDuringPendingStartWaitsThenStops()
    {
        audio.PendingStart = new();
        await using var sut = Create();
        var start = sut.KeyDownAsync(); var stop = sut.KeyUpAsync();
        Assert.Equal(0, audio.Stops);
        audio.PendingStart.SetResult(); await start; await stop;
        Assert.Equal(1, audio.Stops); Assert.Equal(InputState.Idle, sut.State);
    }
    [Fact] public async Task DuplicateReleaseDoesNotRecognizeAgain()
    {
        recognizer.Pending = new();
        await using var sut = Create();
        await sut.KeyDownAsync(); var pending = sut.KeyUpAsync();
        await sut.KeyUpAsync(); recognizer.Pending.SetResult(); await pending;
        Assert.Equal(1, audio.Stops);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task StopOrInjectionFailureReturnsIdleAndAllowsRetry(bool stopFailure)
    {
        audio.FailStop = stopFailure; injector.Fail = !stopFailure;
        await using var sut = Create();
        var failures = new List<Exception>(); sut.Failed += failures.Add;
        await sut.KeyDownAsync(); await sut.KeyUpAsync();
        Assert.NotEmpty(failures); Assert.Equal(InputState.Idle, sut.State);
        audio.FailStop = false; injector.Fail = false;
        await sut.KeyDownAsync(); await sut.KeyUpAsync();
        Assert.Equal("日本語", injector.Text);
    }
    [Fact] public async Task AudioIsClearedEvenWhenRecognitionFails()
    {
        recognizer.Fail = true; await using var sut = Create();
        await sut.KeyDownAsync(); await sut.KeyUpAsync();
        Assert.All(audio.Data.Samples, sample => Assert.Equal(0, sample));
    }
}
