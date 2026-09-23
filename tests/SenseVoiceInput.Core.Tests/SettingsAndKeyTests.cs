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
        var gate = new PushToTalkHoldGate();
        Assert.Empty(gate.Update(false, 0)); Assert.Equal(new[] { true }, gate.Update(true, 1));
        Assert.Empty(gate.Update(true, 2)); Assert.Empty(gate.Update(false, 3));
        Assert.True(gate.FlushRelease(53)); Assert.Empty(gate.Update(false, 54));
    }
    [Fact] public void SettingsRoundTripAndDefaults()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            Assert.Equal("CapsLock", store.Load().PushToTalkKey);
            var settings = new AppSettings { MicrophoneDeviceId = "mic", ModelDirectory = "C:\\models", Backend = RecognitionBackend.CPU, TextInputMode = TextInputMode.Clipboard };
            store.Save(settings);
            Assert.Equal(settings, store.Load());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact] public void CorruptSettingsAreReported()
    {
        string path = Path.GetTempFileName();
        try { File.WriteAllText(path, "broken"); Assert.Throws<System.Text.Json.JsonException>(() => new SettingsStore(path).Load()); }
        finally { File.Delete(path); }
    }
    [Theory] [InlineData(0)] [InlineData(10001)]
    public void InvalidPasteDelayIsRejected(int delay) => Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings { PasteRestoreDelayMs = delay }.Validate());
}
