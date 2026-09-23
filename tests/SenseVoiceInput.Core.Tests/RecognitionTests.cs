using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class RecognitionTests
{
    [Theory] [InlineData(RecognitionBackend.Auto)] [InlineData(RecognitionBackend.CPU)]
    public void CpuAndAutoSelectCpu(RecognitionBackend backend) => Assert.Equal("cpu", RecognitionOptions.Provider(backend));
    [Theory] [InlineData(RecognitionBackend.CUDA)] [InlineData(RecognitionBackend.DirectML)]
    public void UnsupportedBackendIsExplicit(RecognitionBackend backend) => Assert.Throws<NotSupportedException>(() => RecognitionOptions.Provider(backend));
    [Fact] public void ResultRemovesSenseVoiceTags()
    {
        var result = RecognitionOptions.ParseResult("{\"text\":\"<|ja|><|NEUTRAL|><|Speech|>こんにちは<|withitn|>\",\"lang\":\"ja\"}");
        Assert.Equal("こんにちは", result.Text); Assert.Equal("ja", result.Language);
    }
    [Fact] public void LanguageMetadataIsUnwrappedInsteadOfDiscarded()
    {
        Assert.Equal("en", RecognitionOptions.ParseResult("{\"text\":\"hello\",\"lang\":\"<|en|>\"}").Language);
    }
}
