using System.Text.Json;
namespace SenseVoiceInput.Core;
public sealed class ModelProvisioner
{
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly ModelFile[] WhisperFiles = LoadWhisperFiles();
    private static readonly ModelFile VadFile = new("silero_vad.onnx",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx",
        "9E2449E1087496D8D4CABA907F23E0BD3F78D91FA552479BB9C23AC09CBB1FD6");
    private static ModelFile[] LoadWhisperFiles()
    {
        using var stream = typeof(ModelProvisioner).Assembly.GetManifestResourceStream("SenseVoiceInput.Core.whisper-model-manifest.json")!;
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.GetProperty("files").EnumerateArray().Select(f =>
            new ModelFile(f.GetProperty("name").GetString()!, f.GetProperty("url").GetString()!, f.GetProperty("sha256").GetString()!)).ToArray();
    }
    public static bool HasRecognitionModel(AppSettings settings) => settings.Engine == RecognitionEngine.WhisperOnnx &&
        WhisperFiles.All(file => File.Exists(Path.Combine(settings.ModelDirectory, file.Name)));
    public static string ResolveVadPath(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.AutoVoiceInput.Vad.ModelPath)) return Path.GetFullPath(settings.AutoVoiceInput.Vad.ModelPath);
        string legacy = Path.GetFullPath(Path.Combine(settings.ModelDirectory, "..", "silero_vad.onnx"));
        return File.Exists(legacy) ? legacy : ModelPaths.Vad;
    }
    public static bool IsReady(AppSettings settings) => HasRecognitionModel(settings) && File.Exists(ResolveVadPath(settings));
    public async Task<AppSettings> EnsureAsync(AppSettings settings, IProgress<ModelDownloadProgress> progress, CancellationToken ct)
    {
        if (settings.Engine != RecognitionEngine.WhisperOnnx) throw new NotSupportedException("公開版は Whisper のみ対応しています。");
        ct.ThrowIfCancellationRequested();
        var downloader = new ModelDownloadService(Client);
        string directory = settings.ModelDirectory;
        if (!HasRecognitionModel(settings))
        {
            directory = ModelPaths.Whisper;
            ModelNotices.WriteWhisper(directory);
            foreach (var file in WhisperFiles) await downloader.DownloadAsync(file, directory, progress, ct);
        }
        // Repair notices on app-managed installations, including older downloads.
        if (Path.GetFullPath(directory).Equals(Path.GetFullPath(ModelPaths.Whisper), StringComparison.OrdinalIgnoreCase))
            ModelNotices.WriteWhisper(directory);
        string vadPath = ResolveVadPath(settings);
        if (!File.Exists(vadPath))
        {
            ModelNotices.WriteVad(ModelPaths.Models);
            await downloader.DownloadAsync(VadFile, ModelPaths.Models, progress, ct);
            vadPath = ModelPaths.Vad;
        }
        if (Path.GetFullPath(vadPath).Equals(Path.GetFullPath(ModelPaths.Vad), StringComparison.OrdinalIgnoreCase))
            ModelNotices.WriteVad(ModelPaths.Models);
        return settings with { ModelDirectory = directory, AutoVoiceInput = settings.AutoVoiceInput with
            { Vad = settings.AutoVoiceInput.Vad with { ModelPath = vadPath } } };
    }
}
