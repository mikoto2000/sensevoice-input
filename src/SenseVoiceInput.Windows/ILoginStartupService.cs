using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SenseVoiceInput.Windows;

public sealed record LoginStartupStatus(bool Enabled, bool CanChange, string Message);

public interface ILoginStartupService
{
    Task<LoginStartupStatus> GetStatusAsync();
    Task SaveAsync(bool enabled, Action saveSettings);
}

public static class LoginStartupFactory
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, nint name);

    public static ILoginStartupService Create(string executablePath)
    {
        uint length = 0;
        int result = GetCurrentPackageFullName(ref length, 0);
        return result switch
        {
            15700 => new LoginStartupService(executablePath), // APPMODEL_ERROR_NO_PACKAGE
            122 => new PackagedLoginStartupService(new WindowsStartupTask()), // ERROR_INSUFFICIENT_BUFFER
            _ => throw new Win32Exception(result, "アプリのパッケージ情報を確認できません。")
        };
    }
}
