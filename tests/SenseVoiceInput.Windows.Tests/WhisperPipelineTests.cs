using System.Text.Json;
using SenseVoiceInput.Core;
using SenseVoiceInput.Windows;
namespace SenseVoiceInput.Windows.Tests;
public class WhisperPipelineTests
{
    [Fact] public void JapaneseByteLevelTokensDecodeWithoutSpecialTokens()
    {
        var json = JsonSerializer.Serialize(new { model = new { vocab = new Dictionary<string,int> { ["æĹ¥æľ¬"] = 27311, ["èªŀ"] = 31348, ["ãģĵãĤĵãģ«ãģ¡ãģ¯"] = 38088 } }, added_tokens = new[] { new { id = 50258, content = "<|startoftranscript|>", special = true } } });
        var tokenizer = new WhisperTokenizer(json);
        Assert.Equal("こんにちは日本語", tokenizer.Decode([50258,38088,27311,31348]));
        Assert.ThrowsAny<Exception>(() => tokenizer.Decode([999999]));
    }
    [Fact] public void PipelineCallsEachStageAndReturnsMetadata()
    {
        var stages = new List<string>(); var fake = new Stages(stages);
        var pipeline = new WhisperPipeline(fake, fake, fake, fake, new([1,2,3,4],99,100,[],[],4));
        var result = pipeline.Recognize(new(new float[16000],16000), "CPU", default);
        Assert.Equal(new[] { "preprocess", "encode", "decoder", "decode", "decode", "text", "dispose" }, stages);
        Assert.Equal("日本語", result.Text); Assert.Equal("ja", result.Language); Assert.Equal("whisper-onnx", result.Engine);
        Assert.Equal(1, result.AudioDuration.TotalSeconds); Assert.Equal(1, result.GeneratedTokens);
    }
    sealed class Stages(List<string> events) : IWhisperPreprocessor, IWhisperEncoder, IWhisperDecoderFactory, IWhisperTokenizer, IWhisperDecoderContext
    {
        int calls;
        public float[] Process(AudioData audio, CancellationToken ct) { events.Add("preprocess"); return [1]; }
        public float[] Encode(float[] features, CancellationToken ct) { Assert.Equal(1,features[0]); events.Add("encode"); return [2]; }
        public IWhisperDecoderContext Create(float[] hidden) { Assert.Equal(2,hidden[0]); events.Add("decoder"); return this; }
        public float[] Run(long[] input,CancellationToken ct) { events.Add("decode"); var l=new float[100]; l[calls++ == 0 ? 10 : 99]=1; return l; }
        public string Decode(IEnumerable<int> ids) { Assert.Equal(new[]{10},ids); events.Add("text"); return "日本語"; }
        public void Dispose() => events.Add("dispose");
    }
}
