using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using InfraLLM.Core.Interfaces;

namespace InfraLLM.Infrastructure.Services;

public sealed class CredentialEncryptionOptions
{
    // Accepts either:
    // - base64-encoded 32-byte key, OR
    // - an arbitrary string (we derive a 32-byte key via SHA-256)
    public string MasterKey { get; set; } = string.Empty;
}

public sealed class CredentialEncryptionService : ICredentialEncryptionService
{
    private const string Prefix = "v1:";
    private static readonly byte[] Aad = Encoding.UTF8.GetBytes("InfraLLM.Credential.v1");

    private readonly byte[] _key;

    public CredentialEncryptionService(IOptions<CredentialEncryptionOptions> options)
    {
        var masterKey = options.Value.MasterKey;
        if (string.IsNullOrWhiteSpace(masterKey))
            throw new InvalidOperationException("Credential encryption master key not configured (CredentialEncryption:MasterKey)");

        _key = DeriveKey(masterKey);
    }

    public bool IsEncrypted(string value)
        => !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Encrypt(string plaintext)
    {
        if (plaintext == null)
            throw new ArgumentNullException(nameof(plaintext));

        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using (var aes = new AesGcm(_key, 16))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, Aad);
        }

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);

        return Prefix + Convert.ToBase64String(payload);
    }

    public string Decrypt(string ciphertextOrPlaintext)
    {
        if (ciphertextOrPlaintext == null)
            throw new ArgumentNullException(nameof(ciphertextOrPlaintext));

        if (!IsEncrypted(ciphertextOrPlaintext))
            return ciphertextOrPlaintext; // backwards compatible (legacy plaintext)

        var b64 = ciphertextOrPlaintext.Substring(Prefix.Length);
        var payload = Convert.FromBase64String(b64);

        if (payload.Length < 12 + 16)
            throw new CryptographicException("Invalid encrypted credential payload");

        var nonce = payload.AsSpan(0, 12).ToArray();
        var tag = payload.AsSpan(12, 16).ToArray();
        var ciphertext = payload.AsSpan(28).ToArray();

        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(_key, 16))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Aad);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DeriveKey(string masterKey)
    {
        // Prefer a raw 32-byte key if provided as base64.
        try
        {
            var decoded = Convert.FromBase64String(masterKey);
            if (decoded.Length == 32)
                return decoded;
        }
        catch
        {
            // not base64
        }

        // Otherwise derive a fixed-length key from the string.
        return SHA256.HashData(Encoding.UTF8.GetBytes(masterKey));
    }
}
