namespace SenseVoiceInput.Core;

public sealed record AudioData(float[] Samples, int SampleRate);
public sealed record SpeechRecognitionResult(string Text, string Language)
{
    public string Engine { get; init; } = "whisper";
    public string Model { get; init; } = "whisper-large-v3-turbo";
    public string Provider { get; init; } = "CPU";
    public TimeSpan AudioDuration { get; init; }
    public TimeSpan Duration { get; init; }
    public TimeSpan PreprocessDuration { get; init; }
    public TimeSpan EncoderDuration { get; init; }
    public TimeSpan DecoderDuration { get; init; }
    public int GeneratedTokens { get; init; }
    public double RealTimeFactor => AudioDuration.TotalSeconds > 0 ? Duration.TotalSeconds / AudioDuration.TotalSeconds : 0;
}
public interface IAudioCaptureService
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task<AudioData> StopAsync(CancellationToken cancellationToken = default);
}
public interface ISpeechRecognitionService
{
    Task<SpeechRecognitionResult> RecognizeAsync(AudioData audio, CancellationToken cancellationToken = default);
}
public interface ITextInjectionService
{
    Task InjectAsync(string text, nint target, CancellationToken cancellationToken = default);
}
public interface IForegroundWindowService { nint GetForegroundWindow(); }
