using System.Security.Cryptography;

namespace SenseVoiceInput.Core;

public sealed record ModelFile(string Name, string Url, string Sha256);
public sealed record ModelDownloadProgress(string FileName, long BytesReceived, long? TotalBytes);
public static class ModelPaths
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sensevoice-input");
    public static string Models => Path.Combine(Root, "models");
    public static string Whisper => Path.Combine(Models, "whisper-large-v3-turbo");
    public static string Vad => Path.Combine(Models, "silero_vad.onnx");
}

/// <summary>Streams downloads to a temporary file; only verified content replaces the destination.</summary>
public sealed class ModelDownloadService(HttpClient client)
{
    public async Task DownloadAsync(ModelFile file, string directory, IProgress<ModelDownloadProgress>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(file.Name) || file.Name != Path.GetFileName(file.Name) || file.Name is "." or "..")
            throw new ArgumentException("Invalid model filename.");
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, file.Name);
        if (File.Exists(destination))
        {
            progress?.Report(new(file.Name + "（検証中）", 0, null));
            await using var existing = File.OpenRead(destination);
            if (Convert.ToHexString(await SHA256.HashDataAsync(existing, ct)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) return;
        }
        string partial = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            progress?.Report(new(file.Name, 0, null));
            using var response = await client.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[131072];
                long received = 0;
                long lastReport = 0;
                while (true)
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(60));
                    int count = await input.ReadAsync(buffer, timeout.Token);
                    if (count == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    hash.AppendData(buffer, 0, count);
                    received += count;
                    if (Environment.TickCount64 - lastReport >= 200)
                    {
                        progress?.Report(new(file.Name, received, response.Content.Headers.ContentLength));
                        lastReport = Environment.TickCount64;
                    }
                }
                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"モデルの検証に失敗しました: {file.Name}。再試行してください。");
                progress?.Report(new(file.Name, received, response.Content.Headers.ContentLength));
                await output.FlushAsync(ct);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(partial, destination, true);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }
}
