using System.ComponentModel;
using System.Runtime.InteropServices;
using SenseVoiceInput.Core;

namespace SenseVoiceInput.Windows;

public static class CudaRuntimeLoader
{
    private static readonly object LoadLock = new();
    private static readonly Dictionary<string, nint> Modules = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint LoadLibraryEx(string path, nint file, uint flags);

    public static void Activate(string directory)
    {
        directory = Path.GetFullPath(directory);
        var libraries = CudaRuntimeProvisioner.GetLoadPaths(directory);
        lock (LoadLock)
        {
            // MSIX cannot rely on PATH. Resolve dependencies beside each DLL and in
            // System32. Retain the modules for the process lifetime, even across retries.
            foreach (string library in libraries)
            {
                if (Modules.ContainsKey(library)) continue;
                nint module = LoadLibraryEx(library, 0, 0x00000100 | 0x00000800);
                if (module == 0)
                {
                    int code = Marshal.GetLastWin32Error();
                    throw new SpeechRecognitionException(RecognitionError.CudaUnavailable,
                        $"GPU用DLL {Path.GetFileName(library)} を読み込めません（Windows エラー {code}）。モデル・GPUの準備を再試行してください。",
                        new Win32Exception(code));
                }
                Modules.Add(library, module);
            }

            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (!path.Split(Path.PathSeparator).Contains(directory, StringComparer.OrdinalIgnoreCase))
                Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + path, EnvironmentVariableTarget.Process);
        }
    }
}
