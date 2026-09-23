using System.IO;
using NAudio.Wave;
using SenseVoiceInput.Core;
using SenseVoiceInput.Windows;
namespace SenseVoiceInput.Windows.Tests;
public class RecognitionAdapterTests
{
    [Fact] public async Task MissingModelIsReportedBeforeNativeCall()
    {
        using var service = new SenseVoiceRecognitionService(() => Path.Combine(Path.GetTempPath(), "nonexistent-sensevoice-model"), () => RecognitionBackend.CPU);
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.RecognizeAsync(new(new float[16000], 16000)));
    }
    [Fact] public async Task CancelledRequestDoesNotInitializeNativeRuntime()
    {
        using var service = new SenseVoiceRecognitionService(() => "missing", () => RecognitionBackend.CPU);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RecognizeAsync(new(new float[16000], 16000), new CancellationToken(true)));
    }
    [Fact] public async Task VeryShortAudioReturnsEmptyWithoutModel()
    {
        using var service = new SenseVoiceRecognitionService(() => "missing", () => RecognitionBackend.CPU);
        Assert.Equal("", (await service.RecognizeAsync(new([], 16000))).Text);
    }
    [ModelFact] public async Task OfficialJapaneseSampleIsRecognizedLocally()
    {
        string model = Environment.GetEnvironmentVariable("SENSEVOICE_TEST_MODEL")!;
        using var wave = new WaveFileReader(Path.Combine(model, "test_wavs", "ja.wav"));
        byte[] bytes = new byte[checked((int)wave.Length)]; wave.ReadExactly(bytes);
        var audio = new AudioData(PcmConverter.ToMono(bytes, wave.WaveFormat.Channels, wave.WaveFormat.BitsPerSample, false), wave.WaveFormat.SampleRate);
        using var service = new SenseVoiceRecognitionService(() => model, () => RecognitionBackend.CPU);
        var result = await service.RecognizeAsync(audio);
        Assert.Matches("[ぁ-んァ-ン一-龥]", result.Text);
        Assert.DoesNotContain("<|", result.Text);
        Assert.Equal("ja", result.Language);
    }
}
public sealed class ModelFactAttribute : FactAttribute
{
    public ModelFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SENSEVOICE_TEST_MODEL"))) Skip = "Set SENSEVOICE_TEST_MODEL to run real ONNX inference."; }
}
