using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class WhisperSettingsTests
{
    [Fact] public void NewSettingsPreferWhisperCudaJapanese()
    {
        var s=new AppSettings(); Assert.Equal(RecognitionEngine.WhisperOnnx,s.Engine); Assert.Equal(RecognitionBackend.CUDA,s.Backend); Assert.Equal("ja",s.Language); Assert.Contains("whisper-large-v3-turbo",s.ModelDirectory); s.Validate();
    }
    [Fact] public void ExistingSenseVoiceConfigurationKeepsDeviceAndTrigger()
    {
        string path=Path.GetTempFileName();
        try { File.WriteAllText(path,"""{"modelDirectory":"C:/old-model","backend":"Auto","microphoneDeviceId":"mic","pushToTalk":{"enabled":true,"trigger":{"type":"SINGLE_KEY","keys":["F8"]}}}"""); var store=new SettingsStore(path);var s=store.Load();Assert.Equal(RecognitionEngine.SenseVoice,s.Engine);Assert.Equal("mic",s.MicrophoneDeviceId);Assert.Equal(KeyCode.F8,s.PushToTalk.Trigger.Keys[0]);Assert.NotEmpty(store.Warnings); }
        finally { File.Delete(path); }
    }
    [Fact] public void UnsupportedProviderAndLanguageAreRejected()
    {
        Assert.ThrowsAny<ArgumentException>(()=>(new AppSettings {Backend=RecognitionBackend.DirectML}).Validate());
        Assert.ThrowsAny<ArgumentException>(()=>(new AppSettings {Language="en"}).Validate());
    }
}
