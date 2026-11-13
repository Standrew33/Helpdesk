using System.Security.Cryptography;

namespace Helpdesk.IntegrationTests.Infrastructure;

/// <summary>Computes the codes an authenticator app would show (RFC 6238, six digits, 30-second steps, SHA-1).</summary>
public static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string CodeFor(string base32Key, DateTimeOffset? at = null)
    {
        var key = Decode(base32Key.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant());
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;

        var message = new byte[8];

        for (var i = 7; i >= 0; i--)
        {
            message[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        // SHA-1 is what authenticator apps use for the standard six-digit codes; this is not a security use of it.
#pragma warning disable CA5350
        var hash = HMACSHA1.HashData(key, message);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];

        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Decode(string value)
    {
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;

        foreach (var c in value)
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;

            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        return [.. bytes];
    }
}
