namespace SenseVoiceInput.Core;

public sealed record AudioData(float[] Samples, int SampleRate);
public sealed record SpeechRecognitionResult(string Text, string Language);
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
