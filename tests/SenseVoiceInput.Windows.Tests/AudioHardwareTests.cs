using SenseVoiceInput.Windows;
namespace SenseVoiceInput.Windows.Tests;
public class AudioHardwareTests
{
    [HardwareFact] public async Task DefaultMicrophoneProducesPcmAndReleasesDevice()
    {
        using var capture = new AudioCaptureService(() => null);
        await capture.StartAsync();
        await Task.Delay(500);
        var audio = await capture.StopAsync();
        Assert.True(audio.SampleRate > 0);
        Assert.NotEmpty(audio.Samples);
        Assert.All(audio.Samples, sample => Assert.True(float.IsFinite(sample)));
        Array.Clear(audio.Samples);
        await capture.StartAsync();
        await Task.Delay(100);
        var next = await capture.StopAsync();
        Assert.NotEmpty(next.Samples);
        Array.Clear(next.Samples);
    }
}
public sealed class HardwareFactAttribute : FactAttribute
{
    public HardwareFactAttribute() { if (Environment.GetEnvironmentVariable("SENSEVOICE_TEST_MICROPHONE") != "1") Skip = "Opt in with SENSEVOICE_TEST_MICROPHONE=1 to capture microphone audio in memory."; }
}
