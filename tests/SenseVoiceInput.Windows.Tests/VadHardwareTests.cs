using System.Collections.Concurrent;
using SenseVoiceInput.Windows;
namespace SenseVoiceInput.Windows.Tests;
public class VadHardwareTests
{
    [VadHardwareFact] public async Task AutoMonitorProcessesLiveFramesAndReleasesMicrophoneForPtt()
    {
        var queue = new ConcurrentQueue<Action>(); var errors = new List<Exception>(); int delivered = 0;
        using var vad = new WasapiVadService(() => null, () => Environment.GetEnvironmentVariable("SENSEVOICE_TEST_VAD")!, queue.Enqueue);
        vad.Failed += errors.Add;
        vad.SpeechStarted += () => delivered++;
        vad.SegmentReady += data => { delivered++; Array.Clear(data.Samples); };
        vad.Start(800);
        await Task.Delay(600);
        Assert.True(vad.SamplesProcessed > 0);
        vad.Stop();
        // Notifications queued before Stop must not be delivered into the next session.
        while (queue.TryDequeue(out var notification)) notification();
        Assert.Equal(0, delivered); Assert.Empty(errors);
        using var ptt = new AudioCaptureService(() => null);
        await ptt.StartAsync(); await Task.Delay(100); var audio = await ptt.StopAsync();
        Assert.NotEmpty(audio.Samples); Array.Clear(audio.Samples);
        vad.Start(800); await Task.Delay(100); vad.Stop();
    }
}
public sealed class VadHardwareFactAttribute : FactAttribute
{
    public VadHardwareFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SENSEVOICE_TEST_MICROPHONE") != "1" || Environment.GetEnvironmentVariable("SENSEVOICE_TEST_VAD") == null)
            Skip = "Opt in with SENSEVOICE_TEST_MICROPHONE=1 and SENSEVOICE_TEST_VAD.";
    }
}
