using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class SpeechPrerollTests
{
    [Fact] public void KeepsQuietOnsetBeforeDetectedStartWithoutDuplicatingSegment()
    {
        using var buffer = new SpeechPreroll(20, 3);
        buffer.Append(Enumerable.Range(0, 12).Select(i => (float)i).ToArray());
        Assert.Equal(new float[] { 2, 3, 4, 5, 6, 7 }, buffer.Extend(5, [5, 6, 7]));
    }
    [Fact] public void NextUtteranceDoesNotRepeatPreviousWords()
    {
        using var buffer = new SpeechPreroll(20, 5);
        buffer.Append(Enumerable.Range(0, 15).Select(i => (float)i).ToArray());
        buffer.Extend(4, [4, 5, 6, 7]);
        Assert.Equal(new float[] { 8, 9, 10, 11 }, buffer.Extend(10, [10, 11]));
    }
    [Fact] public void RingWrapAndSessionStartClampAvailableAudio()
    {
        using var buffer = new SpeechPreroll(6, 4);
        buffer.Append([0, 1]); Assert.Equal(new float[] { 0, 1 }, buffer.Extend(1, [1]));
        buffer.Append([2, 3, 4, 5, 6, 7, 8, 9]);
        Assert.Equal(new float[] { 4, 5, 6, 7, 8 }, buffer.Extend(7, [7, 8]));
    }
}
