using System.Net;
using System.Security.Cryptography;
using SenseVoiceInput.Core;

namespace SenseVoiceInput.Core.Tests;

public sealed class ModelDownloadTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sensevoice-download-test-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Payload = "verified model fixture"u8.ToArray();
    private static ModelFile FileSpec => new("model.onnx", "https://example.test/model", Convert.ToHexString(SHA256.HashData(Payload)));
    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; ct.ThrowIfCancellationRequested(); return Task.FromResult(response()); }
    }
    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    [Fact] public async Task VerifiedDownloadIsReusedWithoutNetwork()
    {
        using var handler = new Handler(() => Ok(Payload));
        using var client = new HttpClient(handler);
        var service = new ModelDownloadService(client);
        await service.DownloadAsync(FileSpec, directory, null, default);
        await service.DownloadAsync(FileSpec, directory, null, default);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(Path.Combine(directory, FileSpec.Name)));
        Assert.Equal(1, handler.Calls);
        Assert.Empty(Directory.GetFiles(directory, "*.partial"));
    }
    [Fact] public async Task HashMismatchNeverReplacesExistingFileAndRetrySucceeds()
    {
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, FileSpec.Name);
        await File.WriteAllTextAsync(target, "old content");
        using (var client = new HttpClient(new Handler(() => Ok("corrupt"u8.ToArray()))))
            await Assert.ThrowsAsync<InvalidDataException>(() => new ModelDownloadService(client).DownloadAsync(FileSpec, directory, null, default));
        Assert.Equal("old content", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.GetFiles(directory, "*.partial"));
        using var retry = new HttpClient(new Handler(() => Ok(Payload)));
        await new ModelDownloadService(retry).DownloadAsync(FileSpec, directory, null, default);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(target));
    }
    [Fact] public async Task HttpFailureDoesNotCreateFinalFile()
    {
        using var client = new HttpClient(new Handler(() => new(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAsync<HttpRequestException>(() => new ModelDownloadService(client).DownloadAsync(FileSpec, directory, null, default));
        Assert.Empty(Directory.GetFiles(directory));
    }
    [Fact] public async Task CancellationDuringTransferCleansPartialFile()
    {
        using var cancel = new CancellationTokenSource();
        using var client = new HttpClient(new Handler(() => new(HttpStatusCode.OK) { Content = new StreamContent(new CancelStream(cancel)) }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ModelDownloadService(client).DownloadAsync(FileSpec, directory, null, cancel.Token));
        Assert.Empty(Directory.GetFiles(directory));
    }
    private sealed class CancelStream(CancellationTokenSource cancellation) : MemoryStream(Payload)
    {
        private int reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (reads++ == 0) { buffer.Span[0] = Payload[0]; return ValueTask.FromResult(1); }
            cancellation.Cancel();
            return ValueTask.FromCanceled<int>(ct);
        }
    }
    [Fact] public async Task RejectsPathTraversal()
    {
        using var client = new HttpClient(new Handler(() => Ok(Payload)));
        await Assert.ThrowsAsync<ArgumentException>(() => new ModelDownloadService(client).DownloadAsync(FileSpec with { Name = "../escape" }, directory, null, default));
    }
    [Fact] public void DefaultModelLocationIsUnderRequestedProfileFolder()
    {
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sensevoice-input", "models", "whisper-large-v3-turbo"), new AppSettings().ModelDirectory);
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
