using SenseVoiceInput.Core;
using SenseVoiceInput.Windows;
namespace SenseVoiceInput.Windows.Tests;
public class WhisperTests
{
    [Theory] [InlineData(44100, 2)] [InlineData(48000, 1)]
    public void ResamplesToSixteenKhzMono(int rate, int channels)
    {
        var samples = Enumerable.Range(0, rate * channels).Select(i => (float)Math.Sin(2 * Math.PI * 440 * (i / channels) / rate)).ToArray();
        var mono = WhisperAudioPreprocessor.Normalize(samples, rate, channels);
        Assert.InRange(mono.Length, 15990, 16010);
        Assert.InRange(mono.Max(), .9f, 1.01f);
    }
    [Fact] public void NativeMonoDoesNotCopy() { float[] a = [0, .5f, -.5f]; Assert.Same(a, WhisperAudioPreprocessor.Normalize(a, 16000, 1)); }
    [Fact] public void SilenceMelHasExpectedShapeAndNormalization()
    {
        var p = new WhisperAudioPreprocessor(new(16000, 400, 160, 128, 480000, 3000));
        var mel = p.Process(new(new float[16000], 16000), default);
        Assert.Equal(128 * 3000, mel.Length); Assert.All(mel, x => Assert.Equal(-1.5f, x));
    }
    [Fact] public void DecoderStopsAtEos()
    {
        var fake = new FakeStep(10, 20, 99);
        Assert.Equal(new[] { 10, 20 }, WhisperTokenGenerator.Generate(fake, Config(), default));
        Assert.Equal(3, fake.Calls); Assert.Equal(new long[] { 1, 2, 3, 4 }, fake.First);
    }
    [Fact] public void DecoderStopsAtLimit() { var f = new FakeStep(10, 20, 30); Assert.Equal(new[] { 10, 20 }, WhisperTokenGenerator.Generate(f, Config() with { MaxNewTokens = 2 }, default)); }
    [Fact] public void DecoderChecksCancellation() { var f = new FakeStep(10); Assert.ThrowsAny<OperationCanceledException>(() => WhisperTokenGenerator.Generate(f, Config(), new(true))); Assert.Equal(0, f.Calls); }
    [Fact] public void SuppressedTokenIsNotSelected()
    {
        var c = Config() with { SuppressTokens = [10], BeginSuppressTokens = [20], MaxNewTokens = 1 };
        Assert.Equal(new[] { 0 }, WhisperTokenGenerator.Generate(new FakeStep(10), c, default));
    }
    [Fact] public void MelMatchesHuggingFaceReference()
    {
        var audio=new float[16000]; for(int i=0;i<audio.Length;i++) audio[i]=((i*37)%2001-1000)/2000f; audio[0]=.9f;
        var mel=new WhisperAudioPreprocessor(new(16000,400,160,128,480000,3000)).Process(new(audio,16000),default);
        using var stream=typeof(WhisperTests).Assembly.GetManifestResourceStream("SenseVoiceInput.Windows.Tests.Fixtures.whisper-mel-reference.json")!;
        using var doc=System.Text.Json.JsonDocument.Parse(stream);
        foreach(var point in doc.RootElement.EnumerateArray())
        {
            int m=point.GetProperty("mel").GetInt32(), t=point.GetProperty("frame").GetInt32();
            Assert.InRange(Math.Abs(mel[m*3000+t]-point.GetProperty("value").GetSingle()),0,2e-5f);
        }
    }
    [Fact] public void CancellationBetweenDecoderStepsStopsImmediately()
    {
        using var cts=new CancellationTokenSource(); var step=new CancelStep(cts);
        Assert.ThrowsAny<OperationCanceledException>(()=>WhisperTokenGenerator.Generate(step,Config(),cts.Token)); Assert.Equal(1,step.Calls);
    }
    sealed class CancelStep(CancellationTokenSource cts) : IWhisperDecoderStep
    {
        public int Calls;
        public float[] Run(long[] input,CancellationToken ct) { Calls++; cts.Cancel(); return new float[100]; }
    }
    static WhisperGenerationConfig Config() => new([1,2,3,4], 99, 100, [], [], 10);
    sealed class FakeStep(params int[] tokens) : IWhisperDecoderStep
    {
        public int Calls; public long[]? First;
        public float[] Run(long[] input, CancellationToken ct) { First ??= input; var a = new float[100]; a[tokens[Calls++]] = 1; return a; }
    }
}
