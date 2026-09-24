using Microsoft.Win32;

namespace SenseVoiceInput.Windows;

/// <summary>The per-user Run entry is the source of truth; reading never registers startup.</summary>
public sealed class LoginStartupService(
    string executablePath,
    string registryPath = @"Software\Microsoft\Windows\CurrentVersion\Run")
{
    private const string ValueName = "SenseVoiceInput";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(registryPath);
        return key?.GetValue(ValueName) is string command && !string.IsNullOrWhiteSpace(command);
    }

    public void Save(bool enabled, Action saveSettings)
    {
        string command = $"\"{Path.GetFullPath(executablePath)}\" --startup";
        if (enabled && !File.Exists(executablePath))
            throw new FileNotFoundException("自動起動に登録するアプリが見つかりません。", executablePath);

        using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true);
        object? previous = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        RegistryValueKind kind = previous == null ? RegistryValueKind.String : key.GetValueKind(ValueName);
        if (enabled) key.SetValue(ValueName, command, RegistryValueKind.String);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
        try { saveSettings(); }
        catch
        {
            if (previous == null) key.DeleteValue(ValueName, throwOnMissingValue: false);
            else key.SetValue(ValueName, previous, kind);
            throw;
        }
    }
}
