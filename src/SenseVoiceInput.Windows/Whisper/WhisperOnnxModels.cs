using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SenseVoiceInput.Core;
namespace SenseVoiceInput.Windows;
public sealed class WhisperModelSessionFactory
{
    public static readonly string[] RequiredFiles = ["encoder_model_fp16.onnx", "decoder_model_merged_fp16.onnx", "config.json", "generation_config.json", "preprocessor_config.json", "tokenizer.json", "tokenizer_config.json"];
    public static void CheckFiles(string directory)
    {
        foreach (var file in RequiredFiles) if (!File.Exists(Path.Combine(directory,file))) throw new SpeechRecognitionException(RecognitionError.ModelNotFound, $"{file} がありません。Whisper モデルフォルダーを確認してください。");
    }
    public static void ValidateConfig(string directory)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"config.json")));
        var r = doc.RootElement;
        if (r.GetProperty("model_type").GetString() != "whisper" || r.GetProperty("d_model").GetInt32() != 1280 || r.GetProperty("num_mel_bins").GetInt32() != 128 || r.GetProperty("decoder_layers").GetInt32() != 4 || r.GetProperty("decoder_attention_heads").GetInt32() != 20 || r.GetProperty("max_target_positions").GetInt32() != 448)
            throw new InvalidDataException("ModelLoadFailed: large-v3-turbo のモデル設定ではありません。");
    }
    public static InferenceSession Create(string path, RecognitionBackend backend)
    {
        using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8) };
        if (Environment.GetEnvironmentVariable("WHISPER_PROFILE_DIR") is { Length: > 0 } profile)
        {
            Directory.CreateDirectory(profile); options.ProfileOutputPathPrefix = Path.Combine(profile, Path.GetFileNameWithoutExtension(path)); options.EnableProfiling = true;
        }
        if (backend == RecognitionBackend.CUDA)
        {
            try
            {
                if (!OrtEnv.Instance().GetAvailableProviders().Contains("CUDAExecutionProvider")) throw new InvalidOperationException("CUDA EP not included in native runtime.");
                using var cuda = new OrtCUDAProviderOptions();
                cuda.UpdateOptions(new Dictionary<string,string> { ["device_id"] = "0", ["cudnn_conv_algo_search"] = "HEURISTIC", ["cudnn_conv_use_max_workspace"] = "0" });
                options.AppendExecutionProvider_CUDA(cuda);
            }
            catch (Exception e) { throw new SpeechRecognitionException(RecognitionError.CudaUnavailable, "GPUを初期化できません。設定画面の「モデル・GPUの準備を再試行」を実行してください。解消しない場合はNVIDIAドライバーを更新してアプリを再起動するか、BackendをCPUにして保存してください。", e); }
        }
        else if (backend != RecognitionBackend.CPU) throw new ArgumentException("Whisper は CUDA または CPU を指定してください。");
        try { return new InferenceSession(path, options); }
        catch (Exception e) { throw new SpeechRecognitionException(RecognitionError.ModelLoadFailed, "Whisper ONNX モデルをロードできません。モデル構成と GPU メモリを確認してください。", e); }
    }
    internal static IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(InferenceSession session, IReadOnlyCollection<NamedOnnxValue> inputs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var options = new RunOptions();
        using var registration = ct.Register(() => options.Terminate = true);
        try { return session.Run(inputs, session.OutputNames, options); }
        catch (OnnxRuntimeException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
    }
    internal static void Require(InferenceSession session, string name, Type type, params int[] shape)
    {
        if (!session.InputMetadata.TryGetValue(name, out var meta) || meta.ElementType != type || !meta.Dimensions.SequenceEqual(shape))
            throw new InvalidDataException($"ModelLoadFailed: ONNX input {name} の仕様が対応モデルと異なります。");
    }
}
public sealed class WhisperEncoder : IWhisperEncoder
{
    private readonly InferenceSession session;
    public WhisperEncoder(InferenceSession session)
    {
        this.session = session;
        WhisperModelSessionFactory.Require(session,"input_features",typeof(float),-1,128,3000);
    }
    public float[] Encode(float[] features, CancellationToken ct)
    {
        using var output = WhisperModelSessionFactory.Run(session,[NamedOnnxValue.CreateFromTensor("input_features",new DenseTensor<float>(features,new[]{1,128,3000}))],ct);
        ct.ThrowIfCancellationRequested();
        return output.Single(x=>x.Name=="last_hidden_state").AsTensor<float>().ToArray();
    }
}
public sealed class WhisperDecoder : IWhisperDecoderFactory
{
    private readonly InferenceSession session;
    public WhisperDecoder(InferenceSession session)
    {
        this.session = session;
        WhisperModelSessionFactory.Require(session,"input_ids",typeof(long),-1,-1);
        WhisperModelSessionFactory.Require(session,"encoder_hidden_states",typeof(float),-1,-1,1280);
        WhisperModelSessionFactory.Require(session,"use_cache_branch",typeof(bool),1);
        foreach(var name in session.InputNames.Where(x=>x.StartsWith("past_key_values.",StringComparison.Ordinal))) WhisperModelSessionFactory.Require(session,name,typeof(float),-1,20,-1,64);
        if(session.InputNames.Count != 19 || session.OutputNames.Count != 17) throw new InvalidDataException("ModelLoadFailed: merged decoder の cache 構成が不正です。");
    }
    public IWhisperDecoderContext Create(float[] hidden) => new Context(session, hidden);
    private sealed class Context : IWhisperDecoderContext
    {
        private readonly InferenceSession session;
        private readonly float[] hidden;
        private readonly Dictionary<string,DenseTensor<float>> cache = [];
        private bool hasCache;
        public Context(InferenceSession session,float[] hidden)
        {
            this.session=session; this.hidden=hidden;
            foreach(var name in session.InputNames.Where(x=>x.StartsWith("past_key_values.",StringComparison.Ordinal))) cache[name]=new(new[]{1,20,0,64});
        }
        public float[] Run(long[] input, CancellationToken ct)
        {
            List<NamedOnnxValue> feeds = [NamedOnnxValue.CreateFromTensor("input_ids",new DenseTensor<long>(input,new[]{1,input.Length})), NamedOnnxValue.CreateFromTensor("encoder_hidden_states",new DenseTensor<float>(hidden,new[]{1,1500,1280})), NamedOnnxValue.CreateFromTensor("use_cache_branch",new DenseTensor<bool>(new[]{hasCache},new[]{1}))];
            foreach(var (name,value) in cache) feeds.Add(NamedOnnxValue.CreateFromTensor(name,value));
            using var outputs = WhisperModelSessionFactory.Run(session,feeds,ct);
            ct.ThrowIfCancellationRequested();
            var logits = outputs.Single(x=>x.Name=="logits").AsTensor<float>();
            int vocab = logits.Dimensions[^1];
            var next = logits.ToArray().AsSpan(checked((int)logits.Length)-vocab,vocab).ToArray();
            foreach(var output in outputs.Where(x=>x.Name.StartsWith("present.",StringComparison.Ordinal)))
            {
                // Cached branch emits empty cross-attention outputs; retain the first encoder K/V.
                if(hasCache && output.Name.Contains(".encoder.",StringComparison.Ordinal)) continue;
                var value=output.AsTensor<float>(); string name=output.Name.Replace("present.","past_key_values.",StringComparison.Ordinal);
                cache[name].Buffer.Span.Clear(); cache[name]=new(value.ToArray(),value.Dimensions.ToArray());
            }
            hasCache=true; return next;
        }
        public void Dispose() { foreach(var value in cache.Values) value.Buffer.Span.Clear(); cache.Clear(); }
    }
}
