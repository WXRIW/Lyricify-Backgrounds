using Lyricify.Backgrounds.AppleMusicInspired;
using Lyricify.Backgrounds.AppleMusicInspired.Wpf;
using Lyricify.Backgrounds.Demo.Shared;
using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Lyricify.Backgrounds.Demo.Wpf;

public partial class MainWindow : Window
{
    private readonly DemoBackgroundViewModel viewModel = new();
    private readonly DispatcherTimer rebuildTimer;
    private AppleMusicInspiredBackground? embedded;
    private byte[]? artwork;
    private string artworkId = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
        rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        rebuildTimer.Tick += (_, _) => { rebuildTimer.Stop(); RebuildPreview(); };
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        PreviewHost.SizeChanged += (_, _) => PreviewClip.Rect = new Rect(PreviewHost.RenderSize);
        Loaded += (_, _) => RebuildPreview();
        Closed += (_, _) => DisposePreview();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DemoBackgroundViewModel.ArtworkUrl) or nameof(DemoBackgroundViewModel.Status))
            return;

        if (embedded == null)
            return;

        switch (e.PropertyName)
        {
            case nameof(DemoBackgroundViewModel.IsPlaying):
            case nameof(DemoBackgroundViewModel.BassPulseScale):
            case nameof(DemoBackgroundViewModel.BlurScale):
                // The renderer reads these values while producing the next frame.
                return;

            case nameof(DemoBackgroundViewModel.IsVertical):
                embedded.SetVerticalLayout(viewModel.IsVertical);
                return;

            case nameof(DemoBackgroundViewModel.IsBehindLyrics):
                embedded.SetIsBehindLyrics(viewModel.IsBehindLyrics);
                return;

            case nameof(DemoBackgroundViewModel.PortraitControlPointCount):
            case nameof(DemoBackgroundViewModel.PortraitSubdivisionLevels):
                if (!viewModel.IsVertical) return;
                break;

            case nameof(DemoBackgroundViewModel.LandscapeControlPointCount):
            case nameof(DemoBackgroundViewModel.LandscapeSubdivisionLevels):
                if (viewModel.IsVertical) return;
                break;
        }

        rebuildTimer.Stop();
        rebuildTimer.Start();
    }

    private void RebuildPreview()
    {
        DisposePreview();
        embedded = new AppleMusicInspiredBackground(
            viewModel.Settings,
            viewModel.IsLightTheme,
            () => viewModel.IsPlaying,
            presetSlot: viewModel.SelectedPresetIndex - 1);
        embedded.SetVerticalLayout(viewModel.IsVertical, false);
        embedded.SetIsBehindLyrics(viewModel.IsBehindLyrics);
        PreviewHost.Children.Insert(0, embedded);
        _ = ApplyArtworkAsync();
    }

    private void DisposePreview()
    {
        if (embedded != null) PreviewHost.Children.Remove(embedded);
        embedded = null;
    }

    private async void LoadUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            artwork = await AppleMusicInspiredArtworkLoader.LoadFromUriAsync(new Uri(viewModel.ArtworkUrl));
            artworkId = viewModel.ArtworkUrl;
            await ApplyArtworkAsync();
            viewModel.Status = "Artwork loaded.";
        }
        catch (Exception ex) { viewModel.Status = ex.Message; }
    }

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            artwork = await AppleMusicInspiredArtworkLoader.LoadFromFileAsync(dialog.FileName);
            artworkId = dialog.FileName;
            await ApplyArtworkAsync();
            viewModel.Status = "Artwork loaded.";
        }
        catch (Exception ex) { viewModel.Status = ex.Message; }
    }

    private async Task ApplyArtworkAsync()
    {
        if (artwork == null) return;
        using var bitmap = AppleMusicInspiredArtworkLoader.Decode(artwork);
        if (embedded != null) await embedded.SetArtworkAsync(bitmap, artworkId);
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => viewModel.Reset();
}
