using SenseVoiceInput.Core;
namespace SenseVoiceInput.Core.Tests;
public class LoggingTests
{
    [Fact] public void ErrorLogsTypeButNotMessageWhichMayContainSpeech()
    {
        string path = Path.GetTempFileName();
        try
        {
            var logger = new DiagnosticLog(path);
            logger.Error(new IOException("secret dictated text"));
            var log = File.ReadAllText(path);
            Assert.Contains("IOException", log);
            Assert.DoesNotContain("secret", log);
        }
        finally { File.Delete(path); }
    }
}
