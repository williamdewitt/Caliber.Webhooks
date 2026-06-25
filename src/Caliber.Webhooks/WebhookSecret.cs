using System.Security.Cryptography;

namespace Caliber.Webhooks;

/// <summary>
/// Helpers for Standard Webhooks endpoint signing secrets.
/// </summary>
public static class WebhookSecret
{
    private const string Prefix = "whsec_";
    private const int KeyByteLength = 32;

    /// <summary>
    /// Generates a new random endpoint signing secret in the Standard Webhooks <c>whsec_</c> format —
    /// a base64-encoded, cryptographically random 256-bit key.
    /// </summary>
    /// <returns>A fresh secret suitable for <see cref="Endpoint.Secret"/>.</returns>
    public static string Generate()
        => Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyByteLength));

    /// <summary>
    /// Decodes the raw key bytes from a <c>whsec_</c>-prefixed (or bare base64) secret. The caller is
    /// responsible for clearing the returned array once finished with it.
    /// </summary>
    /// <exception cref="ArgumentException">The secret is not valid base64.</exception>
    internal static byte[] DecodeKey(string secret)
    {
        var encoded = secret.StartsWith(Prefix, StringComparison.Ordinal)
            ? secret[Prefix.Length..]
            : secret;

        try
        {
            return Convert.FromBase64String(encoded);
        }
        catch (FormatException ex)
        {
            // Deliberately does not echo the secret value.
            throw new ArgumentException("The endpoint secret is not valid base64.", nameof(secret), ex);
        }
    }
}
