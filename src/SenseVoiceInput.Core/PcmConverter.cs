using System.Buffers.Binary;
namespace SenseVoiceInput.Core;
public static class PcmConverter
{
    public static float[] ToMono(ReadOnlySpan<byte> bytes, int channels, int bits, bool floatingPoint)
    {
        if (channels < 1 || !(floatingPoint && bits == 32 || !floatingPoint && bits == 16))
            throw new ArgumentException("Only float32 and PCM16 audio are supported.");
        int width = bits / 8, frameSize = checked(width * channels);
        if (bytes.Length % frameSize != 0) throw new ArgumentException("Incomplete PCM frame.");
        var mono = new float[bytes.Length / frameSize];
        for (int frame = 0; frame < mono.Length; frame++)
        {
            double sum = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                var sample = bytes.Slice(frame * frameSize + channel * width, width);
                sum += floatingPoint ? BinaryPrimitives.ReadSingleLittleEndian(sample) : BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f;
            }
            mono[frame] = (float)(sum / channels);
        }
        return mono;
    }
}
