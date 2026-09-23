using System.IO.Compression;
using SenseVoiceInput.Core;

namespace SenseVoiceInput.Core.Tests;

public sealed class CudaRuntimeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "cuda-test-" + Guid.NewGuid().ToString("N"));
    private string MakeArchive(params (string Name, string Content)[] entries)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(content);
        }
        return path;
    }
    [Fact] public async Task ExtractsDllsAndNoticesWithoutFollowingArchivePaths()
    {
        string archive = MakeArchive(("../../cudart64_13.dll", "dll"), ("package/LICENSE", "terms"), ("bin/tool.exe", "ignored"), ("bin/unapproved.dll", "ignored"), ("package/NOTICE.txt", "notice"), ("package/COPYING", "copying"));
        string destination = Path.Combine(root, "runtime");
        await CudaRuntimeProvisioner.ExtractAsync(archive, destination, "fixture", default);
        Assert.Equal("dll", File.ReadAllText(Path.Combine(destination, "cudart64_13.dll")));
        Assert.Equal("terms", File.ReadAllText(Path.Combine(destination, "fixture-LICENSE")));
        Assert.Equal("notice", File.ReadAllText(Path.Combine(destination, "fixture-NOTICE.txt")));
        Assert.Equal("copying", File.ReadAllText(Path.Combine(destination, "fixture-COPYING")));
        Assert.Equal(4, Directory.GetFiles(destination).Length);
        Assert.False(File.Exists(Path.Combine(root, "cudart64_13.dll")));
        Assert.False(CudaRuntimeProvisioner.IsReady(destination));
    }
    [Fact] public async Task CancellationKeepsExistingDllAndRetryCompletes()
    {
        string archive = MakeArchive(("bin/cudart64_13.dll", "new"));
        string destination = Path.Combine(root, "runtime");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "cudart64_13.dll"), "old");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CudaRuntimeProvisioner.ExtractAsync(archive, destination, "fixture", new(true)));
        Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "cudart64_13.dll")));
        Assert.Empty(Directory.GetFiles(destination, "*.partial"));
        await CudaRuntimeProvisioner.ExtractAsync(archive, destination, "fixture", default);
        Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "cudart64_13.dll")));
    }
    [Fact] public void IncompleteRuntimeCannotBeActivated() =>
        Assert.Throws<IOException>(() => CudaRuntimeProvisioner.Activate(root));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
