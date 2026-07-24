namespace NetPack.Syntax.Printer;

using System.Text;

/// <summary>Base64 variable-length-quantity encoding, as used by Source Map v3
/// <c>mappings</c> segments. Values are zig-zag encoded (sign in the least
/// significant bit) then split into 5-bit groups with a continuation bit.</summary>
internal static class Base64Vlq
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    public static void Encode(StringBuilder sb, int value)
    {
        var vlq = value < 0 ? ((-value) << 1) | 1 : value << 1;
        do
        {
            var digit = vlq & 0x1F;
            vlq >>= 5;
            if (vlq > 0)
            {
                digit |= 0x20; // continuation bit
            }
            sb.Append(Alphabet[digit]);
        }
        while (vlq > 0);
    }
}
