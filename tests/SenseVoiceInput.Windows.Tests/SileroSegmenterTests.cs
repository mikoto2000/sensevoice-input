using SenseVoiceInput.Windows;
namespace SenseVoiceInput.Windows.Tests;
public class SileroSegmenterTests
{
    [Fact] public void ShortNoiseDoesNotTriggerButSpeechIncludesPrerollAndWaitsForSilence()
    {
        using var vad = new SileroSpeechSegmenter(800);
        float[] frame = Enumerable.Repeat(.1f, 512).ToArray();
        for (int i = 0; i < 32; i++) vad.Accept(frame, 0);
        for (int i = 0; i < 7; i++) vad.Accept(frame, .9f);
        Assert.False(vad.Read().Speech);
        vad.Accept(frame, 0); Assert.Null(vad.Read().Segment);
        for (int i = 0; i < 8; i++) vad.Accept(frame, .9f);
        Assert.True(vad.Read().Speech);
        for (int i = 0; i < 24; i++) vad.Accept(frame, 0);
        Assert.Null(vad.Read().Segment);
        vad.Accept(frame, 0);
        var result = vad.Read();
        Assert.False(result.Speech); Assert.NotNull(result.Segment);
        Assert.Equal(8000 + 8 * 512, result.Segment.Samples.Length);
    }
    [Fact] public void LongSpeechIsCappedAndConsecutiveSegmentsDoNotOverlap()
    {
        using var vad = new SileroSpeechSegmenter(800);
        var delivered = new List<float>();
        for (int n = 0; n < 2000; n++)
        {
            vad.Accept(Enumerable.Range(n * 512, 512).Select(x => (float)x).ToArray(), .9f);
            if (vad.Read().Segment is { } segment)
            {
                Assert.Equal(16000 * 30, segment.Samples.Length);
                delivered.AddRange(segment.Samples);
            }
        }
        Assert.Equal(16000 * 60, delivered.Count);
        Assert.Equal(Enumerable.Range(0, delivered.Count).Select(x => (float)x), delivered);
    }
    [Fact] public void HysteresisKeepsSpeechAndDisposalClearsPendingAudio()
    {
        var vad = new SileroSpeechSegmenter(200);
        for (int i = 0; i < 8; i++) vad.Accept(new float[512], .9f);
        for (int i = 0; i < 40; i++) vad.Accept(new float[512], .4f);
        Assert.True(vad.Read().Speech);
        vad.Dispose();
        Assert.Throws<ObjectDisposedException>(() => vad.Read());
    }
}
