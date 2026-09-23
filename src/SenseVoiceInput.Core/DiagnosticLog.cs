namespace SenseVoiceInput.Core;
public enum DiagnosticEvent { ApplicationStarted, ApplicationStopped, AudioCaptureStarted, AudioCaptureStopped, RecognitionStarted, RecognitionCompleted, TextInjectionStarted, TextInjectionCompleted, AutoSpeechStarted, AutoProcessingStarted }
public sealed class DiagnosticLog(string path)
{
    private readonly object sync = new();
    public Exception? LastWriteError { get; private set; }
    public void Write(DiagnosticEvent value) => Append(value.ToString());
    public void RecognitionInfo(string metadata) => Append(metadata);
    public void Error(Exception error) => Append($"Error: {error.GetType().FullName} Code={(error is SpeechRecognitionException asr ? asr.Code.ToString() : "Other")} HResult=0x{error.HResult:X8}\n{error.StackTrace}");
    private void Append(string line)
    {
        lock (sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                if (File.Exists(path) && new FileInfo(path).Length > 1_048_576) File.Move(path, path + ".1", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}");
                LastWriteError = null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { LastWriteError = e; }
        }
    }
}
