using System.Diagnostics;
using System.IO;
using System.Net.Http;
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
        using var stream = typeof(ModelProvisioner).Assembly.GetManifestResourceStream("SenseVoiceInput.Core.whisper-model-manifest.json")
            ?? throw new InvalidOperationException("モデル定義がありません。");
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.GetProperty("files").EnumerateArray().Select(f =>
            new ModelFile(f.GetProperty("name").GetString()!, f.GetProperty("url").GetString()!, f.GetProperty("sha256").GetString()!)).ToArray();
    }
    public static bool HasRecognitionModel(AppSettings settings) =>
        (settings.Engine == RecognitionEngine.WhisperOnnx ? WhisperFiles.Select(f => f.Name) : ["model.int8.onnx", "tokens.txt"])
        .All(name => File.Exists(Path.Combine(settings.ModelDirectory, name)));
    public static string ResolveVadPath(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.AutoVoiceInput.Vad.ModelPath)) return Path.GetFullPath(settings.AutoVoiceInput.Vad.ModelPath);
        string legacy = Path.GetFullPath(Path.Combine(settings.ModelDirectory, "..", "silero_vad.onnx"));
        return File.Exists(legacy) ? legacy : ModelPaths.Vad;
    }
    public static bool IsReady(AppSettings settings) => HasRecognitionModel(settings) && File.Exists(ResolveVadPath(settings));
    public async Task<AppSettings> EnsureAsync(AppSettings settings, IProgress<ModelDownloadProgress> progress, CancellationToken ct)
    {
        var downloader = new ModelDownloadService(Client);
        string directory = settings.ModelDirectory;
        if (!HasRecognitionModel(settings))
        {
            if (settings.Engine == RecognitionEngine.WhisperOnnx)
            {
                directory = ModelPaths.Whisper;
                foreach (var file in WhisperFiles) await downloader.DownloadAsync(file, directory, progress, ct);
            }
            else
            {
                directory = ModelPaths.SenseVoice;
                await DownloadSenseVoiceAsync(downloader, progress, ct);
            }
        }
        string vadPath = ResolveVadPath(settings);
        if (!File.Exists(vadPath))
        {
            await downloader.DownloadAsync(VadFile, ModelPaths.Models, progress, ct);
            vadPath = ModelPaths.Vad;
        }
        return settings with { ModelDirectory = directory, AutoVoiceInput = settings.AutoVoiceInput with
            { Vad = settings.AutoVoiceInput.Vad with { ModelPath = vadPath } } };
    }
    private static async Task DownloadSenseVoiceAsync(ModelDownloadService downloader, IProgress<ModelDownloadProgress> progress, CancellationToken ct)
    {
        string name = Path.GetFileName(ModelPaths.SenseVoice);
        var archive = new ModelFile("sensevoice.tar.bz2",
            $"https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/{name}.tar.bz2",
            "7D1EFA2138A65B0B488DF37F8B89E3D91A60676E416F515B952358D83DFD347E");
        await downloader.DownloadAsync(archive, ModelPaths.Models, progress, ct);
        string staging = Path.Combine(ModelPaths.Models, "extract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            progress.Report(new("SenseVoice（展開中）", 0, null));
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe"))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            // Extract only the two required members from the checksum-verified archive.
            foreach (string argument in new[] { "-xf", Path.Combine(ModelPaths.Models, archive.Name), "-C", staging, name + "/model.int8.onnx", name + "/tokens.txt" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("モデルを展開できません。");
            var errors = process.StandardError.ReadToEndAsync(ct);
            try { await process.WaitForExitAsync(ct); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); throw; }
            if (process.ExitCode != 0) throw new IOException("モデルの展開に失敗しました: " + await errors);
            Directory.CreateDirectory(ModelPaths.SenseVoice);
            foreach (string file in new[] { "model.int8.onnx", "tokens.txt" })
            {
                ct.ThrowIfCancellationRequested();
                File.Move(Path.Combine(staging, name, file), Path.Combine(ModelPaths.SenseVoice, file), true);
            }
        }
        finally { Directory.Delete(staging, true); }
    }
}
