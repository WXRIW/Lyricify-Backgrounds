namespace Lyricify.Backgrounds.Demo.Shared;

public static class DemoArtworkLoader
{
    private static readonly HttpClient HttpClient = new();

    public static Task<byte[]> LoadFromUriAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return HttpClient.GetByteArrayAsync(uri, cancellationToken);
    }

    public static Task<byte[]> LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllBytesAsync(path, cancellationToken);
    }
}
