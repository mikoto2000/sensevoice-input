using Microsoft.Win32;
using System.IO;
using SenseVoiceInput.Windows;

namespace SenseVoiceInput.Windows.Tests;

public sealed class LoginStartupTests : IDisposable
{
    private readonly string registryPath = @"Software\SenseVoiceInput.Tests\" + Guid.NewGuid().ToString("N");
    private readonly string directory = Path.Combine(Path.GetTempPath(), "SenseVoice startup " + Guid.NewGuid().ToString("N"));
    private readonly string executable;
    private readonly LoginStartupService service;

    public LoginStartupTests()
    {
        Directory.CreateDirectory(directory);
        executable = Path.Combine(directory, "SenseVoiceInput.App.exe");
        File.WriteAllText(executable, "test fixture, never executed");
        service = new(executable, registryPath);
    }

    [Fact]
    public void EnableQuotesPathAndDisablePreservesOtherValues()
    {
        Assert.False(service.IsEnabled());
        using (var missing = Registry.CurrentUser.OpenSubKey(registryPath)) Assert.Null(missing);
        bool saved = false;
        service.Save(true, () => saved = true);
        Assert.True(saved);
        Assert.True(service.IsEnabled());
        using (var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true)!)
        {
            Assert.Equal($"\"{executable}\" --startup", key.GetValue("SenseVoiceInput"));
            key.SetValue("Unrelated", "keep");
        }
        service.Save(false, () => { });
        service.Save(false, () => { });
        Assert.False(service.IsEnabled());
        using var result = Registry.CurrentUser.OpenSubKey(registryPath)!;
        Assert.Equal("keep", result.GetValue("Unrelated"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSettingsSaveRestoresPreviousRegistration(bool initiallyEnabled)
    {
        if (initiallyEnabled) service.Save(true, () => { });
        Assert.Throws<IOException>(() => service.Save(!initiallyEnabled, () => throw new IOException("Save failed")));
        Assert.Equal(initiallyEnabled, service.IsEnabled());
    }

    [Fact]
    public void MissingExecutableCannotRegisterButCanUnregister()
    {
        service.Save(true, () => { });
        File.Delete(executable);
        Assert.Throws<FileNotFoundException>(() => service.Save(true, () => Assert.Fail("Must not save")));
        service.Save(false, () => { });
        Assert.False(service.IsEnabled());
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
        File.Delete(executable);
        Directory.Delete(directory);
    }
}
