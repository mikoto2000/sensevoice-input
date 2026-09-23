namespace SenseVoiceInput.Core;
/// <summary>Bounded session audio indexed by absolute sample position. Never crosses sessions.</summary>
public sealed class SpeechPreroll : IDisposable
{
    private readonly float[] ring;
    private readonly int prefixSamples;
    private long total, previousEnd;
    public SpeechPreroll(int capacitySamples, int prefixSamples)
    {
        if (capacitySamples <= 0 || prefixSamples < 0 || prefixSamples > capacitySamples) throw new ArgumentOutOfRangeException(nameof(capacitySamples));
        ring = new float[capacitySamples]; this.prefixSamples = prefixSamples;
    }
    public void Append(ReadOnlySpan<float> samples)
    {
        foreach (float sample in samples) { ring[(int)(total % ring.Length)] = sample; total++; }
    }
    public float[] Extend(long segmentStart, float[] segment)
    {
        long start = Math.Max(Math.Max(0, total - ring.Length), Math.Max(previousEnd, segmentStart - prefixSamples));
        int prefix = (int)Math.Max(0, segmentStart - start);
        var result = new float[checked(prefix + segment.Length)];
        for (int i = 0; i < prefix; i++) result[i] = ring[(int)((start + i) % ring.Length)];
        segment.CopyTo(result, prefix);
        previousEnd = segmentStart + segment.Length;
        return result;
    }
    public void Dispose() { Array.Clear(ring); total = previousEnd = 0; }
}
