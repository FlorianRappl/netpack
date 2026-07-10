namespace NetPack;

using System.Security.Cryptography;
using System.Text;

public static class Hash
{
    public static async Task<string> ComputeHash(Stream stream)
    {
        stream.Position = 0;
        var hash = await GetHashSha256(stream);
        stream.Position = 0;
        var sb = new StringBuilder();

        for (var i = 0; i < 3; i++)
        {
            var b = hash[i];
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }

    private static Task<byte[]> GetHashSha256(Stream stream)
    {
        var sha256 = SHA256.Create();
        return sha256.ComputeHashAsync(stream);
    }

    /// <summary>
    /// A short, stable hex digest of <paramref name="value"/> (default 6 chars).
    /// Used e.g. to derive per-file suffixes for CSS-module class names.
    /// </summary>
    public static string Short(string value, int length = 6)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var sb = new StringBuilder(length);

        for (var i = 0; sb.Length < length && i < bytes.Length; i++)
        {
            sb.Append(bytes[i].ToString("x2"));
        }

        return sb.ToString()[..length];
    }
}
