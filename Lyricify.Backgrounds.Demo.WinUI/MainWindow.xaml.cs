using Lyricify.Backgrounds;
using Lyricify.Backgrounds.AppleMusicInspired;
using Lyricify.Backgrounds.Demo.Shared;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.ComponentModel;
using Windows.Graphics;
using WinUIBackground = Lyricify.Backgrounds.AppleMusicInspired.WinUI.AppleMusicInspiredBackground;

namespace Lyricify.Backgrounds.Demo.WinUI;

public sealed partial class MainWindow : Window
{
    private WinUIBackground? preview;
    private byte[]? artwork;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Lyricify Backgrounds Demo · WinUI";
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += (_, _) => preview?.Dispose();
        RebuildPreview();
    }

    public DemoBackgroundViewModel ViewModel { get; } = new();

    public void CenterOnScreen()
    {
        DisplayArea displayArea = DisplayArea.GetFromWindowId(
            AppWindow.Id,
            DisplayAreaFallback.Primary);
        RectInt32 workArea = displayArea.WorkArea;
        SizeInt32 size = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - size.Width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - size.Height) / 2)));
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DemoBackgroundViewModel.Status) or nameof(DemoBackgroundViewModel.ArtworkUrl))
            return;
        if (e.PropertyName == nameof(DemoBackgroundViewModel.SelectedPresetIndex))
        {
            preview?.SetPreset(ViewModel.SelectedPresetIndex - 1);
            return;
        }
        ApplyState();
    }

    private void RebuildPreview()
    {
        preview?.Dispose();
        PreviewHost.Children.Clear();
        preview = new WinUIBackground(ViewModel.Settings, ViewModel.SelectedPresetIndex - 1);
        preview.FirstFramePresented += (_, _) => SetStatus("The shared HLSL renderer is active.");
        preview.Faulted += (_, e) => SetStatus(e.Exception.Message);
        PreviewHost.Children.Add(preview);
        ApplyState();
        if (artwork != null) _ = preview.SetArtworkAsync(artwork);
    }

    private void ApplyState()
    {
        preview?.ApplySettings(ViewModel.Settings);
        preview?.UpdateState(new BackgroundState
        {
            IsPlaying = ViewModel.IsPlaying,
            IsVertical = ViewModel.IsVertical,
            IsLightTheme = ViewModel.IsLightTheme,
            IsBehindLyrics = ViewModel.IsBehindLyrics,
            IsVisible = true,
        });
    }

    private async void LoadUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            artwork = await AppleMusicInspiredArtworkLoader.LoadFromUriAsync(new Uri(ArtworkUrlBox.Text));
            SetStatus("Artwork bytes loaded.");
            if (preview != null) await preview.SetArtworkAsync(artwork);
            SetStatus("Artwork loaded.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new global::Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            global::Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
            if (file == null) return;
            artwork = await AppleMusicInspiredArtworkLoader.LoadFromFileAsync(file.Path);
            SetStatus("Artwork bytes loaded.");
            if (preview != null) await preview.SetArtworkAsync(artwork);
            SetStatus("Artwork loaded.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Reset();
        ApplyState();
    }

    private void SetStatus(string value)
    {
        if (DispatcherQueue.HasThreadAccess)
            ViewModel.Status = value;
        else
            DispatcherQueue.TryEnqueue(() => ViewModel.Status = value);
    }
}
