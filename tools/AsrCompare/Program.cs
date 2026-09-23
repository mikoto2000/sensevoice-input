using System.Diagnostics;
using System.Text.Json;
using NAudio.Wave;
using SenseVoiceInput.Core;
using SenseVoiceInput.Windows;
if(args.Length<3) { Console.Error.WriteLine("Usage: AsrCompare <whisper-dir> <CUDA|CPU> <wav> [wav...]. Prints recognized text for explicit development comparison only."); return 2; }
using var cts=new CancellationTokenSource(); Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cts.Cancel();};
var provider=Enum.Parse<RecognitionBackend>(args[1],true);
using var whisper=new WhisperOnnxRecognitionService(args[0],provider,Console.Error.WriteLine);
foreach(var file in args.Skip(2))
{
    using var reader=new WaveFileReader(file); var bytes=new byte[checked((int)reader.Length)]; reader.ReadExactly(bytes);
    var samples=PcmConverter.ToMono(bytes,reader.WaveFormat.Channels,reader.WaveFormat.BitsPerSample,reader.WaveFormat.Encoding==WaveFormatEncoding.IeeeFloat);
    var audio=new AudioData(samples,reader.WaveFormat.SampleRate);
    try
    {
        foreach(var (engine,recognizer) in new (string,ISpeechRecognitionService?)[] {("Whisper cold/current",whisper),("Whisper reused",whisper)})
        {
            if(recognizer==null) continue;
            var watch=Stopwatch.StartNew(); var result=await recognizer.RecognizeAsync(audio,cts.Token);
            Console.WriteLine(JsonSerializer.Serialize(new { wav=Path.GetFileName(file),engine,provider=result.Provider,text=result.Text,audioSeconds=(double)samples.Length/audio.SampleRate,wallSeconds=watch.Elapsed.TotalSeconds,wallRtf=watch.Elapsed.TotalSeconds/((double)samples.Length/audio.SampleRate),recognitionSeconds=result.Duration>TimeSpan.Zero ? result.Duration.TotalSeconds : (double?)null,inferenceRtf=result.Duration>TimeSpan.Zero ? result.RealTimeFactor : (double?)null,result.GeneratedTokens },new JsonSerializerOptions { Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
    }
    finally { Array.Clear(samples);Array.Clear(bytes); }
}
return 0;
