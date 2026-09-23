using System.Windows;
namespace SenseVoiceInput.App;
public partial class App : Application
{
    private ApplicationSession? session;
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\SenseVoiceInput", out bool first);
        if (!first) { MessageBox.Show("SenseVoice Input は既に起動しています。トレイアイコンを開いてください。"); Shutdown(); return; }
        try { session = new ApplicationSession(this, e.Args); session.Start(); }
        catch (Exception error)
        {
            MessageBox.Show($"起動できませんでした。\n{error.Message}", "SenseVoice Input");
            session?.Dispose(); Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        session?.Dispose(); instance?.Dispose(); base.OnExit(e);
    }
}
