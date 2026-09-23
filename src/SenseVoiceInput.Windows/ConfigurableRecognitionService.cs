using SenseVoiceInput.Core;
namespace SenseVoiceInput.Windows;
/// <summary>Both PTT and AUTO use this single serialized recognizer. Sessions survive across requests.</summary>
public sealed class ConfigurableRecognitionService(Func<AppSettings> settings, Action<string>? diagnostic = null) : ISpeechRecognitionService, IDisposable
{
    private readonly SemaphoreSlim gate = new(1,1);
    private ISpeechRecognitionService? current;
    private (RecognitionEngine Engine,string Path,RecognitionBackend Backend)? key;
    private bool disposed;
    public async Task<SpeechRecognitionResult> RecognizeAsync(AudioData audio,CancellationToken cancellationToken=default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            var s=settings(); var next=(s.Engine,System.IO.Path.GetFullPath(s.ModelDirectory),s.Backend);
            if(key!=next)
            {
                (current as IDisposable)?.Dispose(); current=null; key=null;
                current=s.Engine switch
                {
                    RecognitionEngine.WhisperOnnx=>new WhisperOnnxRecognitionService(next.Item2,s.Backend,diagnostic),
                    RecognitionEngine.SenseVoice=>new SenseVoiceRecognitionService(()=>next.Item2,()=>s.Backend),
                    _=>throw new ArgumentOutOfRangeException(nameof(s.Engine))
                };
                key=next;
            }
            return await current!.RecognizeAsync(audio,cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) { diagnostic?.Invoke("Recognition Cancelled"); throw; }
        finally { gate.Release(); }
    }
    public void Dispose()
    {
        gate.Wait();
        try { if(disposed) return; disposed=true; (current as IDisposable)?.Dispose(); current=null; }
        finally { gate.Release(); }
    }
}
