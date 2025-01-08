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
}
