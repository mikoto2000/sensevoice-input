using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class TriggerSettingsTests
{
    [Fact] public void InvalidPttDisablesOnlyPtt()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"pushToTalk":{"enabled":true,"trigger":{"type":"SINGLE_KEY","keys":["NOT_A_KEY"]}},"autoVoiceInput":{"enabled":true,"toggleTrigger":{"type":"DOUBLE_TAP","keys":["LEFT_CTRL"]}}}""");
            var store = new SettingsStore(path); var s = store.Load();
            Assert.False(s.PushToTalk.Enabled); Assert.True(s.AutoVoiceInput.Enabled); Assert.NotEmpty(store.Warnings);
        }
        finally { File.Delete(path); }
    }
    [Fact] public void BrokenJsonDisablesBothInsteadOfEnablingDefaultKeys()
    {
        var path = Path.GetTempFileName();
        try { File.WriteAllText(path, "broken"); var store = new SettingsStore(path); var s = store.Load(); Assert.False(s.PushToTalk.Enabled); Assert.False(s.AutoVoiceInput.Enabled); Assert.NotEmpty(store.Warnings); }
        finally { File.Delete(path); }
    }
    [Fact] public void ConflictingSettingsCannotBeSaved()
    {
        var s = new AppSettings { PushToTalk = new() { Trigger = InputTrigger.Single(KeyCode.F12) }, AutoVoiceInput = new() { Enabled = true, ToggleTrigger = InputTrigger.Single(KeyCode.F12) } };
        Assert.Throws<ArgumentException>(s.Validate);
    }
    [Fact] public void OldCapsConfigurationRequiresNewSelection()
    {
        var path = Path.GetTempFileName();
        try { File.WriteAllText(path, """{"pushToTalkKey":"CapsLock"}"""); var store = new SettingsStore(path); Assert.False(store.Load().PushToTalk.Enabled); Assert.NotEmpty(store.Warnings); }
        finally { File.Delete(path); }
    }
}
