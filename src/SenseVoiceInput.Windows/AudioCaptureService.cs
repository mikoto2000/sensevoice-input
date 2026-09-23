using NAudio.CoreAudioApi;
using NAudio.Wave;
using SenseVoiceInput.Core;

namespace SenseVoiceInput.Windows;

public sealed class AudioCaptureService(Func<string?> deviceId) : IAudioCaptureService, IDisposable
{
    private WasapiCapture? capture;
    private MMDevice? device;
    private MemoryStream? buffer;
    private TaskCompletionSource? stopped;
    private Exception? captureError;
    private readonly object sync = new();
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (capture != null) throw new InvalidOperationException("Capture already active.");
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var id = deviceId();
            device = id == null ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications) : enumerator.GetDevice(id);
            capture = new WasapiCapture(device);
            capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(capture.WaveFormat.SampleRate, capture.WaveFormat.Channels);
            buffer = new MemoryStream();
            captureError = null;
            stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
            capture.DataAvailable += OnData;
            capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception != null) captureError = e.Exception;
                stopped.TrySetResult();
            };
            capture.StartRecording();
            return Task.CompletedTask;
        }
        catch { Dispose(); throw; }
    }
    private void OnData(object? sender, WaveInEventArgs e)
    {
        lock (sync)
        {
            if (buffer == null || capture == null || captureError != null) return;
            if (buffer.Length + e.BytesRecorded > capture.WaveFormat.AverageBytesPerSecond * 60L)
            {
                captureError = new InvalidOperationException("Recording exceeded the 60 second limit.");
                capture.StopRecording();
                return;
            }
            buffer.Write(e.Buffer, 0, e.BytesRecorded);
        }
    }
    public async Task<AudioData> StopAsync(CancellationToken cancellationToken = default)
    {
        if (capture == null) return new([], 16000);
        try
        {
            if (!stopped!.Task.IsCompleted) capture.StopRecording();
            // Always finish device cleanup, even during application cancellation.
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellationToken.ThrowIfCancellationRequested();
            if (captureError != null) throw new IOException("Microphone capture failed.", captureError);
            lock (sync)
            {
                var format = capture.WaveFormat;
                return new(PcmConverter.ToMono(buffer!.GetBuffer().AsSpan(0, (int)buffer.Length), format.Channels, 32, true), format.SampleRate);
            }
        }
        finally { Dispose(); }
    }
    public void Dispose()
    {
        // Dispose outside the buffer lock: NAudio may join the capture thread.
        capture?.Dispose(); capture = null;
        device?.Dispose(); device = null;
        lock (sync)
        {
            if (buffer != null) { Array.Clear(buffer.GetBuffer()); buffer.Dispose(); buffer = null; }
        }
    }
    public static IReadOnlyList<MicrophoneDevice> GetDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<MicrophoneDevice> { new(null, "Default microphone") };
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        { using (endpoint) result.Add(new(endpoint.ID, endpoint.FriendlyName)); }
        return result;
    }
}
public sealed record MicrophoneDevice(string? Id, string Name);
