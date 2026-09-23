using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class SettingsAndKeyTests
{
    [Fact] public void KeyGateIgnoresRepeatAndUnmatchedRelease()
    {
        var gate = new PushToTalkKeyGate();
        Assert.False(gate.Update(false)); Assert.True(gate.Update(true));
        Assert.False(gate.Update(true)); Assert.True(gate.Update(false)); Assert.False(gate.Update(false));
    }
    [Fact] public void SettingsRoundTripAndDefaults()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            Assert.Equal("CapsLock", store.Load().PushToTalkKey);
            var settings = new AppSettings { MicrophoneDeviceId = "mic", ModelDirectory = "C:\\models", Backend = RecognitionBackend.CPU };
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
