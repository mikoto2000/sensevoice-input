using System.ComponentModel;
using System.Windows;
namespace SenseVoiceInput.App;
public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); }
    protected override void OnClosing(CancelEventArgs e) { e.Cancel = true; Hide(); base.OnClosing(e); }
}
