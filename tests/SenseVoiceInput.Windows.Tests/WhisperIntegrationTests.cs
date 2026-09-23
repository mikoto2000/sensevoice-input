using System.IO;
using NAudio.Wave;
using SenseVoiceInput.Core;
using SenseVoiceInput.Windows;
using Xunit.Abstractions;
namespace SenseVoiceInput.Windows.Tests;
public class WhisperIntegrationTests(ITestOutputHelper output)
{
    [Fact] public async Task MissingModelHasDistinctError()
    {
        using var s=new WhisperOnnxRecognitionService("missing-whisper",RecognitionBackend.CPU);
        var e=await Assert.ThrowsAsync<SpeechRecognitionException>(()=>s.RecognizeAsync(new(new float[16000],16000)));
        Assert.Equal(RecognitionError.ModelNotFound,e.Code);
    }
    [Fact] public async Task CancelBeforeLoad()
    {
        using var s=new WhisperOnnxRecognitionService("missing-whisper",RecognitionBackend.CUDA);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>s.RecognizeAsync(new(new float[16000],16000),new(true)));
    }
    [WhisperFact] public async Task JapaneseWaveAndSessionReuse()
    {
        var model=Environment.GetEnvironmentVariable("WHISPER_TEST_MODEL")!;
        var wav=Environment.GetEnvironmentVariable("WHISPER_TEST_WAV")!;
        var backend=Environment.GetEnvironmentVariable("WHISPER_TEST_PROVIDER")=="CPU" ? RecognitionBackend.CPU : RecognitionBackend.CUDA;
        using var reader=new WaveFileReader(wav); var bytes=new byte[checked((int)reader.Length)]; reader.ReadExactly(bytes);
        var audio=new AudioData(PcmConverter.ToMono(bytes,reader.WaveFormat.Channels,reader.WaveFormat.BitsPerSample,false),reader.WaveFormat.SampleRate);
        using var service=new WhisperOnnxRecognitionService(model,backend,output.WriteLine);
        for(int i=0;i<2;i++)
        {
            var result=await service.RecognizeAsync(audio);
            output.WriteLine($"Run {i}: {result.Text} / {result.Duration.TotalSeconds:F3}s RTF={result.RealTimeFactor:F3}");
            Assert.Matches("[ぁ-んァ-ン一-龥]",result.Text); Assert.DoesNotContain("<|",result.Text); Assert.Equal(backend.ToString(),result.Provider);
        }
    }
}
public sealed class WhisperFactAttribute : FactAttribute
{
    public WhisperFactAttribute() { if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WHISPER_TEST_MODEL")) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WHISPER_TEST_WAV"))) Skip="Set WHISPER_TEST_MODEL and WHISPER_TEST_WAV for real inference."; }
}
