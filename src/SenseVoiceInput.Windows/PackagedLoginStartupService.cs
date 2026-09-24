using Windows.ApplicationModel;

namespace SenseVoiceInput.Windows;

public interface IStartupTaskControl
{
    Task<StartupTaskState> GetStateAsync();
    Task<StartupTaskState> RequestEnableAsync();
    Task DisableAsync();
}

public sealed class WindowsStartupTask : IStartupTaskControl
{
    public const string TaskId = "SenseVoiceInputStartup";
    public async Task<StartupTaskState> GetStateAsync() => (await StartupTask.GetAsync(TaskId)).State;
    public async Task<StartupTaskState> RequestEnableAsync() => await (await StartupTask.GetAsync(TaskId)).RequestEnableAsync();
    public async Task DisableAsync() => (await StartupTask.GetAsync(TaskId)).Disable();
}

/// <summary>The OS is authoritative. Never fall back to a Run key for packaged apps.</summary>
public sealed class PackagedLoginStartupService(IStartupTaskControl task) : ILoginStartupService
{
    private const string WindowsSettingsHelp = "Windows の「設定 → アプリ → スタートアップ」からも変更できます。そこで無効にした場合は、同じ画面で SenseVoice Input を有効に戻してください。";

    public static LoginStartupStatus Describe(StartupTaskState state) => state switch
    {
        StartupTaskState.Disabled => new(false, true, "自動起動は無効です。チェックを入れて保存すると、次回ログイン時から通知領域に常駐します。" + WindowsSettingsHelp),
        StartupTaskState.Enabled => new(true, true, "自動起動は有効です。次回ログイン時から通知領域に常駐します。" + WindowsSettingsHelp),
        StartupTaskState.DisabledByUser => new(false, false, "Windows 側で無効になっているため、アプリからは変更できません。Windows の「設定 → アプリ → スタートアップ」で SenseVoice Input を有効に戻してください。"),
        StartupTaskState.DisabledByPolicy => new(false, false, "管理者のポリシー、またはこの環境の制限で自動起動が無効です。アプリからは変更できません。"),
        StartupTaskState.EnabledByPolicy => new(true, false, "管理者のポリシーで自動起動が有効です。アプリからは変更できません。"),
        _ => throw new InvalidOperationException("自動起動の状態を確認できません。")
    };

    public async Task<LoginStartupStatus> GetStatusAsync() => Describe(await task.GetStateAsync());

    public async Task SaveAsync(bool enabled, Action saveSettings)
    {
        var before = await task.GetStateAsync();
        var status = Describe(before);
        bool changed = status.Enabled != enabled;
        if (changed && !status.CanChange) throw new InvalidOperationException(status.Message);
        if (changed)
        {
            if (enabled)
            {
                var result = await task.RequestEnableAsync();
                var after = Describe(result);
                if (!after.Enabled) throw new InvalidOperationException("自動起動を有効にできませんでした。" + after.Message);
            }
            else await task.DisableAsync();
            var actual = Describe(await task.GetStateAsync());
            if (actual.Enabled != enabled) throw new InvalidOperationException("自動起動の変更が反映されませんでした。" + actual.Message);
        }
        try { saveSettings(); }
        catch (Exception saveError)
        {
            if (changed)
            {
                try
                {
                    // Restore only an ordinary state changed by us. Respect subsequent OS/user changes.
                    var current = await task.GetStateAsync();
                    if (before == StartupTaskState.Disabled && current == StartupTaskState.Enabled) await task.DisableAsync();
                    else if (before == StartupTaskState.Enabled && current == StartupTaskState.Disabled)
                    {
                        var restored = await task.RequestEnableAsync();
                        if (!Describe(restored).Enabled) throw new InvalidOperationException(Describe(restored).Message);
                    }
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("設定を保存できず、自動起動の状態も復元できませんでした。Windows のスタートアップ設定を確認してください。", saveError, rollbackError);
                }
            }
            throw;
        }
    }
}
