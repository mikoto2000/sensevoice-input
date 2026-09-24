using Microsoft.ML.OnnxRuntime;
using SenseVoiceInput.Core;
using SenseVoiceInput.Windows;

namespace SenseVoiceInput.Windows.Tests;

public sealed class CudaRuntimeIntegrationTests
{
    [CudaSetupFact] public void DirectLaunchDiscoversRuntimeAndRegistersCudaProvider()
    {
        string? original = Environment.GetEnvironmentVariable("PATH");
        try
        {
            // No run.ps1 or externally supplied CUDA path.
            Environment.SetEnvironmentVariable("PATH", Environment.GetFolderPath(Environment.SpecialFolder.System));
            CudaDriverCheck.Verify();
            string runtime = Assert.IsType<string>(CudaRuntimeProvisioner.FindExisting(AppContext.BaseDirectory));
            CudaRuntimeProvisioner.Activate(runtime);
            // Provider registration must work without PATH lookup (as in MSIX).
            Environment.SetEnvironmentVariable("PATH", Environment.GetFolderPath(Environment.SpecialFolder.System));
            CudaRuntimeProvisioner.Activate(runtime);
            Environment.SetEnvironmentVariable("PATH", Environment.GetFolderPath(Environment.SpecialFolder.System));
            using var options = new SessionOptions();
            using var cuda = new OrtCUDAProviderOptions();
            cuda.UpdateOptions(new Dictionary<string, string> { ["device_id"] = "0" });
            options.AppendExecutionProvider_CUDA(cuda);
        }
        finally { Environment.SetEnvironmentVariable("PATH", original); }
    }
}
public sealed class CudaSetupFactAttribute : FactAttribute
{
    public CudaSetupFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CUDA_SETUP_TEST") != "1")
            Skip = "Set CUDA_SETUP_TEST=1 on a CUDA 13 GPU machine with local redistributables.";
    }
}
