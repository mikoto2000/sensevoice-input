namespace SenseVoiceInput.Core;
/// <summary>Offline upstream terms, for the official app-managed downloads only.</summary>
public static class ModelNotices
{
    public static void WriteWhisper(string directory) => Write(directory, "Whisper-LICENSE.txt", "Whisper-SOURCE.txt");
    public static void WriteVad(string directory) => Write(directory, "Silero-VAD-LICENSE.txt", "Silero-VAD-SOURCE.txt");
    private static void Write(string directory, params string[] files)
    {
        Directory.CreateDirectory(directory);
        foreach (string file in files)
        {
            using var resource = typeof(ModelNotices).Assembly.GetManifestResourceStream("SenseVoiceInput.Core.licenses." + file)
                ?? throw new InvalidOperationException("Missing model license resource: " + file);
            using var reader = new StreamReader(resource);
            string text = reader.ReadToEnd(), target = Path.Combine(directory, file);
            if (File.Exists(target) && File.ReadAllText(target) == text) continue;
            string partial = target + "." + Guid.NewGuid().ToString("N") + ".partial";
            try { File.WriteAllText(partial, text); File.Move(partial, target, true); }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
    }
}
