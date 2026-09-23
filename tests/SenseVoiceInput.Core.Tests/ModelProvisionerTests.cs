using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public sealed class ModelProvisionerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "model-provision-test-" + Guid.NewGuid().ToString("N"));
    private AppSettings Settings(RecognitionEngine engine)
    {
        string dir = Path.Combine(root, "custom");
        Directory.CreateDirectory(dir);
        return new AppSettings { Engine = engine, ModelDirectory = dir, Backend = RecognitionBackend.CPU };
    }
    [Theory]
    [InlineData(RecognitionEngine.WhisperOnnx)]
    [InlineData(RecognitionEngine.SenseVoice)]
    public async Task CompleteCustomModelsAreReusedWithoutDownloadingOrChangingOtherSettings(RecognitionEngine engine)
    {
        var settings = Settings(engine);
        string[] files = engine == RecognitionEngine.WhisperOnnx
            ? ["encoder_model_fp16.onnx", "decoder_model_merged_fp16.onnx", "config.json", "generation_config.json", "preprocessor_config.json", "tokenizer.json", "tokenizer_config.json"]
            : ["model.int8.onnx", "tokens.txt"];
        foreach (string name in files) File.WriteAllText(Path.Combine(settings.ModelDirectory, name), "existing user model");
        string vad = Path.Combine(root, "silero_vad.onnx");
        File.WriteAllText(vad, "existing VAD");
        Assert.True(ModelProvisioner.IsReady(settings));
        var result = await new ModelProvisioner().EnsureAsync(settings, new Progress<ModelDownloadProgress>(), default);
        Assert.Equal(settings.ModelDirectory, result.ModelDirectory);
        Assert.Equal(vad, result.AutoVoiceInput.Vad.ModelPath);
        Assert.Equal(settings.PushToTalk, result.PushToTalk);
        Assert.Equal(settings.Backend, result.Backend);
        File.Delete(Path.Combine(settings.ModelDirectory, files[0]));
        Assert.False(ModelProvisioner.IsReady(settings));
    }
    [Fact] public void MissingExplicitVadDoesNotSilentlyUseAnotherExistingModel()
    {
        var settings = Settings(RecognitionEngine.WhisperOnnx);
        string explicitPath = Path.Combine(root, "selected-vad.onnx");
        settings = settings with { AutoVoiceInput = new() { Vad = new() { ModelPath = explicitPath } } };
        File.WriteAllText(Path.Combine(root, "silero_vad.onnx"), "legacy");
        Assert.Equal(explicitPath, ModelProvisioner.ResolveVadPath(settings));
        Assert.False(ModelProvisioner.IsReady(settings));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
