using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lyricify.Backgrounds
{
    public interface IBackgroundSettings
    {
    }

    public interface IBackgroundProvider
    {
        string Id { get; }
        string DisplayName { get; }
        IBackgroundSettings CreateDefaultSettings();
    }

    public interface IBackgroundSession : IDisposable
    {
        event EventHandler FirstFramePresented;
        event EventHandler<BackgroundFaultedEventArgs> Faulted;

        bool IsReady { get; }
        void UpdateState(BackgroundState state);
        Task SetArtworkAsync(
            BackgroundArtwork artwork,
            CancellationToken cancellationToken = default);
    }

    public sealed class BackgroundState
    {
        public bool IsPlaying { get; set; }
        public bool IsVertical { get; set; }
        public bool IsLightTheme { get; set; }
        public bool IsBehindLyrics { get; set; }
        public bool IsVisible { get; set; } = true;

        public BackgroundState Clone() => (BackgroundState)MemberwiseClone();
    }

    public sealed class BackgroundArtwork
    {
        public BackgroundArtwork(string id, byte[] encodedData)
        {
            Id = id ?? string.Empty;
            EncodedData = encodedData ?? throw new ArgumentNullException(nameof(encodedData));
        }

        public string Id { get; }
        public byte[] EncodedData { get; }
    }

    public sealed class BackgroundFaultedEventArgs : EventArgs
    {
        public BackgroundFaultedEventArgs(Exception exception) =>
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));

        public Exception Exception { get; }
    }
}
