using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class SettingsAndKeyTests
{
    [Fact] public void OldSettingsDefaultToDirectInput()
    {
        string path = Path.GetTempFileName();
        try { File.WriteAllText(path, "{}"); Assert.Equal(TextInputMode.Unicode, new SettingsStore(path).Load().TextInputMode); }
        finally { File.Delete(path); }
    }
    [Fact] public void InvalidInputModeIsRejected() => Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings { TextInputMode = (TextInputMode)99 }.Validate());
    [Fact] public void KeyGateIgnoresRepeatAndUnmatchedRelease()
    {
        var gate = new InputTriggerMatcher(InputTrigger.Single(KeyCode.F12));
        Assert.Equal(TriggerTransition.None, gate.OnKeyEvent(new(KeyCode.F12, KeyAction.Up, 0)));
        Assert.Equal(TriggerTransition.Activated, gate.OnKeyEvent(new(KeyCode.F12, KeyAction.Down, 1)));
        Assert.Equal(TriggerTransition.None, gate.OnKeyEvent(new(KeyCode.F12, KeyAction.Down, 2)));
        Assert.Equal(TriggerTransition.Released, gate.OnKeyEvent(new(KeyCode.F12, KeyAction.Up, 3)));
    }
    [Fact] public void SettingsRoundTripAndDefaults()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            Assert.Equal(KeyCode.F12, store.Load().PushToTalk.Trigger.Keys[0]);
            var settings = new AppSettings { MicrophoneDeviceId = "mic", ModelDirectory = "C:\\models", Backend = RecognitionBackend.CPU, TextInputMode = TextInputMode.Clipboard };
            store.Save(settings);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(settings), System.Text.Json.JsonSerializer.Serialize(store.Load()));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact] public void CorruptSettingsAreReported()
    {
        string path = Path.GetTempFileName();
        try { File.WriteAllText(path, "broken"); var store = new SettingsStore(path); Assert.False(store.Load().PushToTalk.Enabled); Assert.NotEmpty(store.Warnings); }
        finally { File.Delete(path); }
    }
    [Theory] [InlineData(0)] [InlineData(10001)]
    public void InvalidPasteDelayIsRejected(int delay) => Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings { PasteRestoreDelayMs = delay }.Validate());
}
