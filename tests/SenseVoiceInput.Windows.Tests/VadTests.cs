using NAudio.Wave;
using System.IO;
using SenseVoiceInput.Windows;
namespace SenseVoiceInput.Windows.Tests;
public class VadTests
{
    [VadModelFact] public void CapturePacketSizeDoesNotChangeExtractedSpeech()
    {
        using var reader = new AudioFileReader(Path.Combine(Environment.GetEnvironmentVariable("SENSEVOICE_TEST_MODEL")!, "test_wavs", "ja.wav"));
        var raw = new float[(int)(reader.Length / sizeof(float))]; int count = reader.Read(raw, 0, raw.Length);
        var mono = new float[count / reader.WaveFormat.Channels];
        for (int i = 0; i < mono.Length; i++) for (int c = 0; c < reader.WaveFormat.Channels; c++) mono[i] += raw[i * reader.WaveFormat.Channels + c] / reader.WaveFormat.Channels;
        var samples = new float[16000].Concat(new VadResampler(reader.WaveFormat.SampleRate).Convert(mono)).Concat(new float[32000]).ToArray();
        float[] Extract(int packet)
        {
            using var vad = new SileroVadEngine(Environment.GetEnvironmentVariable("SENSEVOICE_TEST_VAD")!, 800);
            var output = new List<float>();
            for (int i = 0; i < samples.Length; i += packet)
            {
                var result = vad.Accept(samples.AsSpan(i, Math.Min(packet, samples.Length - i)).ToArray());
                if (result.Segment is { } s) { output.AddRange(s.Samples); Array.Clear(s.Samples); }
            }
            return output.ToArray();
        }
        var expected = Extract(512); Assert.NotEmpty(expected); Assert.Equal(expected, Extract(1600));
    }
    [Fact] public void MissingModelFailsBeforeNativeCall() => Assert.Throws<FileNotFoundException>(() => new SileroVadEngine("missing-vad.onnx", 800));
    [Fact] public void StreamingResamplerKeepsRateAndFiniteSamples()
    {
        var r = new VadResampler(48000); var count = 0;
        for (int i = 0; i < 100; i++) { var data = r.Convert(Enumerable.Repeat(0.1f, 480).ToArray()); count += data.Length; Assert.All(data, v => Assert.True(float.IsFinite(v))); }
        Assert.InRange(count, 15900, 16100);
    }
    [VadModelFact] public void NativeVadSeparatesSpeechAndSilence()
    {
        var model = Environment.GetEnvironmentVariable("SENSEVOICE_TEST_VAD");
        var asr = Environment.GetEnvironmentVariable("SENSEVOICE_TEST_MODEL");
        using var vad = new SileroVadEngine(model!, 800);
        for (int i = 0; i < 50; i++) { var result = vad.Accept(new float[512]); Assert.False(result.Speech); Assert.Null(result.Segment); }
        using var reader = new AudioFileReader(Path.Combine(asr!, "test_wavs", "ja.wav"));
        var raw = new float[(int)(reader.Length / sizeof(float))]; var n = reader.Read(raw, 0, raw.Length);
        var mono = new float[n / reader.WaveFormat.Channels];
        for (int i = 0; i < mono.Length; i++) for (int c = 0; c < reader.WaveFormat.Channels; c++) mono[i] += raw[i * reader.WaveFormat.Channels + c] / reader.WaveFormat.Channels;
        var resampler = new VadResampler(reader.WaveFormat.SampleRate);
        var samples = resampler.Convert(mono).Concat(new float[32000]).ToArray();
        bool speech = false; int segments = 0;
        for (int i = 0; i + 512 <= samples.Length; i += 512)
        {
            var result = vad.Accept(samples.AsSpan(i, 512).ToArray()); speech |= result.Speech;
            if (result.Segment is { } segment) { Assert.NotEmpty(segment.Samples); Assert.Equal(16000, segment.SampleRate); Array.Clear(segment.Samples); segments++; }
        }
        Assert.True(speech); Assert.True(segments > 0);
    }
}
public sealed class VadModelFactAttribute : FactAttribute
{
    public VadModelFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SENSEVOICE_TEST_VAD") == null || Environment.GetEnvironmentVariable("SENSEVOICE_TEST_MODEL") == null)
            Skip = "Opt in with SENSEVOICE_TEST_VAD and SENSEVOICE_TEST_MODEL.";
    }
}
