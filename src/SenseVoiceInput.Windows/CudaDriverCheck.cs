using System.Runtime.InteropServices;
using SenseVoiceInput.Core;

namespace SenseVoiceInput.Windows;

public static class CudaDriverCheck
{
    [DllImport("nvcuda.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int cuInit(uint flags);
    [DllImport("nvcuda.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int cuDeviceGetCount(out int count);
    [DllImport("nvcuda.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int cuDriverGetVersion(out int version);

    public static void Verify()
    {
        try
        {
            if (cuInit(0) != 0 || cuDeviceGetCount(out int count) != 0 || count == 0 ||
                cuDriverGetVersion(out int version) != 0 || version < 13000)
                throw new InvalidOperationException("CUDA 13 driver unavailable.");
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            throw new SpeechRecognitionException(RecognitionError.CudaUnavailable,
                "CUDA 13対応のNVIDIA GPU・ドライバーを利用できません。NVIDIAドライバーを更新してアプリを再起動するか、設定のBackendをCPUにして保存してください。", e);
        }
    }
}
