using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class AudioTests
{
    [Fact] public void StereoFloatIsAveragedToMono()
    {
        var bytes = new[] { 1f, 0f, -1f, 0f }.SelectMany(BitConverter.GetBytes).ToArray();
        Assert.Equal(new[] { .5f, -.5f }, PcmConverter.ToMono(bytes, 2, 32, true));
    }
    [Fact] public void Pcm16IsNormalized()
    {
        Assert.Equal(new[] { -1f, 0f }, PcmConverter.ToMono([0, 128, 0, 0], 1, 16, false));
    }
    [Fact] public void IncompleteFrameIsRejected() => Assert.Throws<ArgumentException>(() => PcmConverter.ToMono([0], 1, 16, false));
}
