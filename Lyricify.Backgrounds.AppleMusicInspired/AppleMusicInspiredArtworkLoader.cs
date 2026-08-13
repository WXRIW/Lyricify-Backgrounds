using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Lyricify.Backgrounds.AppleMusicInspired
{
    public static class AppleMusicInspiredArtworkLoader
    {
        private static readonly HttpClient Client = new();

        public static async Task<byte[]> LoadFromUriAsync(
            Uri uri,
            CancellationToken cancellationToken = default)
        {
            if (uri is null) throw new ArgumentNullException(nameof(uri));
            return await Client.GetByteArrayAsync(uri, cancellationToken).ConfigureAwait(false);
        }

        public static Task<byte[]> LoadFromFileAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            File.ReadAllBytesAsync(path, cancellationToken);

        public static Bitmap Decode(byte[] encodedData)
        {
            if (encodedData is null) throw new ArgumentNullException(nameof(encodedData));
            using var stream = new MemoryStream(encodedData, writable: false);
            using var decoded = new Bitmap(stream);
            return new Bitmap(decoded);
        }
    }
}
