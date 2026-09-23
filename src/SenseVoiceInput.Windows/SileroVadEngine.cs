using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SenseVoiceInput.Core;
namespace SenseVoiceInput.Windows;
public readonly record struct VadFrameResult(bool Speech, AudioData? Segment);

/// <summary>CPU-only Silero v4 ONNX inference. Input is mono PCM at 16 kHz.</summary>
public sealed class SileroVadEngine : IDisposable
{
    private readonly InferenceSession session;
    private readonly SileroSpeechSegmenter segmenter;
    private readonly float[] frame = new float[512], h = new float[128], c = new float[128];
    private readonly bool exported;
    private int frameCount;
    private bool disposed;
    public SileroVadEngine(string model, int silenceTimeoutMs)
    {
        if (!File.Exists(model)) throw new FileNotFoundException("Silero VADモデルがありません。download-vad-model.ps1を実行してください。", model);
        using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        session = new InferenceSession(Path.GetFullPath(model), options);
        exported = session.InputMetadata.ContainsKey("x");
        string[] inputs = exported ? ["x", "h", "c"] : ["input", "sr", "h", "c"];
        string[] outputs = exported ? ["prob", "new_h", "new_c"] : ["output", "hn", "cn"];
        if (!inputs.ToHashSet().SetEquals(session.InputMetadata.Keys) || !outputs.ToHashSet().SetEquals(session.OutputMetadata.Keys))
        {
            session.Dispose();
            throw new InvalidDataException("この公開版は同梱 manifest で指定した Silero VAD v4 モデルに対応しています。");
        }
        segmenter = new(silenceTimeoutMs);
    }
    public VadFrameResult Accept(float[] samples)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        for (int offset = 0; offset < samples.Length;)
        {
            int count = Math.Min(frame.Length - frameCount, samples.Length - offset);
            Array.Copy(samples, offset, frame, frameCount, count);
            frameCount += count; offset += count;
            if (frameCount != frame.Length) continue;
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(exported ? "x" : "input", new DenseTensor<float>(frame, [1, 512])),
                NamedOnnxValue.CreateFromTensor("h", new DenseTensor<float>(h, [2, 1, 64])),
                NamedOnnxValue.CreateFromTensor("c", new DenseTensor<float>(c, [2, 1, 64]))
            };
            if (!exported) inputs.Add(NamedOnnxValue.CreateFromTensor("sr", new DenseTensor<long>(new long[] { 16000 }, [1])));
            using var output = session.Run(inputs);
            float probability = output.Single(v => v.Name == (exported ? "prob" : "output")).AsTensor<float>().First();
            output.Single(v => v.Name == (exported ? "new_h" : "hn")).AsTensor<float>().ToArray().CopyTo(h, 0);
            output.Single(v => v.Name == (exported ? "new_c" : "cn")).AsTensor<float>().ToArray().CopyTo(c, 0);
            segmenter.Accept(frame, probability);
            frameCount = 0;
        }
        return segmenter.Read();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; session.Dispose(); segmenter.Dispose();
        Array.Clear(frame); Array.Clear(h); Array.Clear(c);
    }
}

/// <summary>Frame-based hysteresis, 250 ms onset, 500 ms preroll, and a 30 s output cap.</summary>
public sealed class SileroSpeechSegmenter(int silenceTimeoutMs) : IDisposable
{
    private const int Rate = 16000, Frame = 512, MaxSamples = Rate * 30;
    private readonly float[] ring = new float[MaxSamples + Rate];
    private readonly int silenceSamples = Math.Clamp(silenceTimeoutMs, 200, 5000) * 16;
    private readonly Queue<AudioData> ready = new();
    private long total, previousEnd, candidate = -1, start = -1, silence = -1;
    private bool disposed;
    public void Accept(ReadOnlySpan<float> samples, float probability)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (samples.Length != Frame || !float.IsFinite(probability)) throw new ArgumentException("Invalid VAD frame.");
        long frameStart = total;
        foreach (float value in samples) ring[(int)(total++ % ring.Length)] = value;
        if (start < 0)
        {
            if (probability >= .5f)
            {
                if (candidate < 0) candidate = frameStart;
                if (total - candidate >= Rate / 4)
                    start = Math.Max(previousEnd, Math.Max(0, candidate - Rate / 2));
            }
            else candidate = -1;
        }
        if (start < 0) return;
        if (probability >= .35f) silence = -1;
        else if (silence < 0) silence = frameStart;
        if (total - start >= MaxSamples) Complete(start + MaxSamples);
        else if (silence >= 0 && total - silence >= silenceSamples) Complete(silence);
    }
    private void Complete(long end)
    {
        var audio = new float[checked((int)(end - start))];
        for (int i = 0; i < audio.Length; i++) audio[i] = ring[(int)((start + i) % ring.Length)];
        ready.Enqueue(new(audio, Rate)); previousEnd = end;
        start = candidate = silence = -1;
    }
    public VadFrameResult Read()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new(start >= 0, ready.TryDequeue(out var audio) ? audio : null);
    }
    public void Dispose()
    {
        disposed = true; Array.Clear(ring);
        while (ready.TryDequeue(out var audio)) Array.Clear(audio.Samples);
    }
}
