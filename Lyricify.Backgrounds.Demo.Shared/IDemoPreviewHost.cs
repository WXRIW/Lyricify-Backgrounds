using Lyricify.Backgrounds.AppleMusicInspired;

namespace Lyricify.Backgrounds.Demo.Shared;

public interface IDemoPreviewHost : IDisposable
{
    Task ApplyAsync(DemoBackgroundViewModel viewModel, CancellationToken cancellationToken = default);
    Task SetArtworkAsync(byte[] encodedData, string id, CancellationToken cancellationToken = default);
}
