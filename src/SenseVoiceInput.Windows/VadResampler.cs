using NAudio.Dsp;
namespace SenseVoiceInput.Windows;
public sealed class VadResampler
{
    private readonly WdlResampler resampler = new();
    private readonly int inputRate;
    public VadResampler(int inputRate)
    {
        this.inputRate = inputRate;
        resampler.SetMode(true, 2, false); resampler.SetFilterParms(); resampler.SetFeedMode(true); resampler.SetRates(inputRate, 16000);
    }
    public float[] Convert(float[] mono)
    {
        if (inputRate == 16000) return (float[])mono.Clone();
        int needed = resampler.ResamplePrepare(mono.Length, 1, out var input, out int offset);
        if (needed != mono.Length) throw new InvalidOperationException("Unexpected resampler input size.");
        mono.CopyTo(input, offset);
        var output = new float[(int)Math.Ceiling(mono.Length * 16000.0 / inputRate) + 64];
        int count = resampler.ResampleOut(output, 0, mono.Length, output.Length, 1);
        Array.Resize(ref output, count); return output;
    }
}
