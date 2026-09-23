using System.IO;
using System.Numerics;
using System.Text.Json;
using MathNet.Numerics.IntegralTransforms;
using SenseVoiceInput.Core;
namespace SenseVoiceInput.Windows;
public sealed record WhisperPreprocessorConfig(int SampleRate, int FftSize, int HopLength, int MelBins, int Samples, int Frames)
{
    public static WhisperPreprocessorConfig Load(string directory)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "preprocessor_config.json")));
        var r = doc.RootElement; int I(string n) => r.GetProperty(n).GetInt32();
        var c = new WhisperPreprocessorConfig(I("sampling_rate"), I("n_fft"), I("hop_length"), I("feature_size"), I("n_samples"), I("nb_max_frames"));
        if (c != new WhisperPreprocessorConfig(16000, 400, 160, 128, 480000, 3000) || r.GetProperty("padding_value").GetDouble() != 0 || r.GetProperty("padding_side").GetString() != "right")
            throw new InvalidDataException("ModelLoadFailed: large-v3-turbo の前処理設定と一致しません。");
        return c;
    }
}
public interface IWhisperPreprocessor { float[] Process(AudioData audio, CancellationToken ct); }
public sealed class WhisperAudioPreprocessor : IWhisperPreprocessor
{
    private readonly WhisperPreprocessorConfig config;
    private readonly double[] window;
    private readonly double[,] filters;
    public WhisperAudioPreprocessor(WhisperPreprocessorConfig config)
    {
        this.config = config;
        window = Enumerable.Range(0, config.FftSize).Select(i => .5 - .5 * Math.Cos(2 * Math.PI * i / config.FftSize)).ToArray();
        filters = new double[config.MelBins, config.FftSize / 2 + 1];
        // Slaney scale and area normalization, matching WhisperFeatureExtractor.
        static double ToMel(double hz) => hz < 1000 ? hz / (200.0 / 3) : 15 + Math.Log(hz / 1000) / (Math.Log(6.4) / 27);
        static double ToHz(double mel) => mel < 15 ? mel * (200.0 / 3) : 1000 * Math.Exp((mel - 15) * (Math.Log(6.4) / 27));
        var edges = Enumerable.Range(0, config.MelBins + 2).Select(i => ToHz(ToMel(config.SampleRate / 2.0) * i / (config.MelBins + 1))).ToArray();
        for (int m = 0; m < config.MelBins; m++) for (int k = 0; k <= config.FftSize / 2; k++)
        {
            double hz = k * (double)config.SampleRate / config.FftSize;
            filters[m,k] = Math.Max(0, Math.Min((hz - edges[m]) / (edges[m+1] - edges[m]), (edges[m+2] - hz) / (edges[m+2] - edges[m+1]))) * 2 / (edges[m+2] - edges[m]);
        }
    }
    public static float[] Normalize(float[] samples, int rate, int channels)
    {
        if (rate < 8000 || rate > 192000 || channels < 1 || samples.Length % channels != 0 || samples.Any(x => !float.IsFinite(x))) throw new ArgumentException("AudioPreprocessingFailed: 音声形式が不正です。");
        var mono = samples;
        if (channels > 1)
        {
            mono = new float[samples.Length / channels];
            for (int i = 0; i < mono.Length; i++) { double sum = 0; for (int c = 0; c < channels; c++) sum += samples[i * channels + c]; mono[i] = (float)(sum / channels); }
        }
        return rate == 16000 ? mono : new VadResampler(rate).Convert(mono);
    }
    public float[] Process(AudioData audio, CancellationToken ct)
    {
        float[] mono = Normalize(audio.Samples, audio.SampleRate, 1);
        var padded = new float[config.Samples]; Array.Copy(mono, padded, Math.Min(mono.Length, padded.Length));
        var result = new float[config.MelBins * config.Frames];
        var fft = new Complex[config.FftSize]; var power = new double[config.FftSize / 2 + 1]; float max = float.NegativeInfinity;
        try
        {
            for (int t = 0; t < config.Frames; t++)
            {
                ct.ThrowIfCancellationRequested();
                for (int i = 0; i < fft.Length; i++)
                {
                    int index = t * config.HopLength + i - config.FftSize / 2;
                    if (index < 0) index = -index;
                    if (index >= padded.Length) index = 2 * padded.Length - 2 - index;
                    fft[i] = new(padded[index] * window[i], 0);
                }
                Fourier.Forward(fft, FourierOptions.AsymmetricScaling);
                for (int k = 0; k < power.Length; k++) power[k] = fft[k].Real * fft[k].Real + fft[k].Imaginary * fft[k].Imaginary;
                for (int m = 0; m < config.MelBins; m++)
                {
                    double sum = 0; for (int k = 0; k < power.Length; k++) sum += filters[m,k] * power[k];
                    float log = (float)Math.Log10(Math.Max(1e-10, sum)); result[m * config.Frames + t] = log; max = Math.Max(max, log);
                }
            }
            for (int i = 0; i < result.Length; i++) result[i] = (Math.Max(result[i], max - 8) + 4) / 4;
            return result;
        }
        finally { Array.Clear(padded); if (!ReferenceEquals(mono, audio.Samples)) Array.Clear(mono); }
    }
}
