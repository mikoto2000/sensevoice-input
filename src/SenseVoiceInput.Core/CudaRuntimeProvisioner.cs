using System.IO.Compression;
using System.Text.Json;

namespace SenseVoiceInput.Core;

/// <summary>Installs checksum-verified NVIDIA redistributables without changing system settings.</summary>
public sealed class CudaRuntimeProvisioner
{
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
    public static string InstallDirectory => Path.Combine(ModelPaths.Root, "cuda-runtime");
    private static readonly string[] RequiredDlls = ["cudart64_13.dll", "cublas64_13.dll", "cublasLt64_13.dll",
        "cufft64_12.dll", "nvrtc64_130_0.dll", "nvrtc-builtins64_130.dll", "cudnn64_9.dll",
        "cudnn_adv64_9.dll", "cudnn_cnn64_9.dll", "cudnn_ops64_9.dll", "cudnn_graph64_9.dll",
        "cudnn_heuristic64_9.dll", "cudnn_engines_precompiled64_9.dll", "cudnn_engines_runtime_compiled64_9.dll"];
    private static readonly string[] RequiredNotices = ["cuda_cudart-LICENSE", "libcublas-LICENSE", "libcufft-LICENSE", "cuda_nvrtc-LICENSE", "cudnn-LICENSE"];
    public static bool IsReady(string directory) => RequiredDlls.Concat(RequiredNotices).All(name => File.Exists(Path.Combine(directory, name)));
    public static string? FindExisting(string baseDirectory)
    {
        foreach (string candidate in new[] { InstallDirectory, Path.Combine(baseDirectory, "cuda-runtime"), baseDirectory })
            if (IsReady(candidate)) return candidate;
        // Development builds launched directly from bin also reuse the script's existing installation.
        for (var parent = new DirectoryInfo(baseDirectory); parent != null; parent = parent.Parent)
        {
            if (!File.Exists(Path.Combine(parent.FullName, "scripts", "cuda-runtime-manifest.json"))) continue;
            string candidate = Path.Combine(parent.FullName, "artifacts", "cuda-runtime");
            if (IsReady(candidate)) return candidate;
        }
        return null;
    }
    public static void Activate(string directory)
    {
        if (!IsReady(directory)) throw new IOException("GPU用ファイルが不足しています。設定画面で準備を再試行してください。");
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!path.Split(Path.PathSeparator).Contains(directory, StringComparer.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + path, EnvironmentVariableTarget.Process);
    }
    public async Task<string> EnsureAsync(string baseDirectory, IProgress<ModelDownloadProgress> progress, CancellationToken ct)
    {
        if (FindExisting(baseDirectory) is { } existing) return existing;
        using var stream = typeof(CudaRuntimeProvisioner).Assembly.GetManifestResourceStream("SenseVoiceInput.Core.cuda-runtime-manifest.json")!;
        using var manifest = JsonDocument.Parse(stream);
        string cache = Path.Combine(ModelPaths.Root, "cuda-downloads");
        var downloader = new ModelDownloadService(Client);
        foreach (var entry in manifest.RootElement.EnumerateArray())
        {
            string name = entry.GetProperty("name").GetString()!;
            var archive = new ModelFile(name + ".zip", entry.GetProperty("url").GetString()!, entry.GetProperty("sha256").GetString()!);
            await downloader.DownloadAsync(archive, cache, progress, ct);
            progress.Report(new($"GPU: {name}（展開中）", 0, null));
            await ExtractAsync(Path.Combine(cache, archive.Name), InstallDirectory, name, ct);
        }
        if (!IsReady(InstallDirectory)) throw new IOException("GPU用ファイルを展開できませんでした。再試行してください。");
        return InstallDirectory;
    }
    // Flatten only DLLs and notices. Archive paths never become destination paths.
    public static async Task ExtractAsync(string archivePath, string destination, string package, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(package) || package != Path.GetFileName(package) || package is "." or "..")
            throw new ArgumentException("Invalid package name.", nameof(package));
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(archivePath);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            string name = Path.GetFileName(entry.FullName.Replace('\\', '/'));
            bool dll = RequiredDlls.Contains(name, StringComparer.OrdinalIgnoreCase);
            bool notice = new[] { "LICENSE", "EULA", "NOTICE", "COPYING", "COPYRIGHT" }.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
            if (!dll && !notice) continue;
            string outputName = dll ? name : package + "-" + name;
            if (!names.Add(outputName)) throw new InvalidDataException("GPUアーカイブ内のファイル名が重複しています。");
            string output = Path.Combine(destination, outputName);
            string partial = output + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                await using (var input = entry.Open())
                await using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                    await input.CopyToAsync(target, ct);
                ct.ThrowIfCancellationRequested();
                File.Move(partial, output, true);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
    }
}
