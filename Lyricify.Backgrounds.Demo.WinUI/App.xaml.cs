using Microsoft.UI.Xaml;

namespace Lyricify.Backgrounds.Demo.WinUI;

public partial class App : Application
{
    private MainWindow? window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.CenterOnScreen();
        window.Activate();
    }
}
