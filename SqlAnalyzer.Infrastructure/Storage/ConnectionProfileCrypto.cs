using System.Security.Cryptography;
using System.Text;

namespace SqlAnalyzer.Infrastructure.Storage;

internal static class ConnectionProfileCrypto
{
    private const string LocalPayloadPrefix = "v3:";
    private const string PortablePayloadPrefix = "portable:";
    private const string AuthenticatedPayloadPrefix = "v2:";
    private const int LocalKeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("SqlAnalyzer.Next::Profiles::20260323");
    private static readonly byte[] LegacyEncryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes("SqlAnalyzer.Next::Profiles::Encryption::20260427"));
    private static readonly byte[] LegacyMacKey = SHA256.HashData(Encoding.UTF8.GetBytes("SqlAnalyzer.Next::Profiles::Mac::20260427"));
    public static string Encrypt(string? plainText)
    {
        string value = plainText ?? string.Empty;
        if (value.Length == 0)
        {
            return string.Empty;
        }

        byte[] plainBytes = Encoding.UTF8.GetBytes(value);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        byte[] tag = new byte[TagBytes];
        byte[] cipherBytes = new byte[plainBytes.Length];

        using AesGcm aes = new(GetOrCreateLocalKey(), TagBytes);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        byte[] payload = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, payload, nonce.Length + tag.Length, cipherBytes.Length);
        return LocalPayloadPrefix + Convert.ToBase64String(payload);
    }
    public static string EncodePortable(string? plainText)
    {
        string value = plainText ?? string.Empty;
        return value.Length == 0
            ? string.Empty
            : PortablePayloadPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }
    public static string Decrypt(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
        {
            return string.Empty;
        }

        if (cipherText.StartsWith(LocalPayloadPrefix, StringComparison.Ordinal))
        {
            return DecryptLocal(cipherText[LocalPayloadPrefix.Length..]);
        }

        if (cipherText.StartsWith(PortablePayloadPrefix, StringComparison.Ordinal))
        {
            return DecodePortable(cipherText[PortablePayloadPrefix.Length..]);
        }

        if (cipherText.StartsWith(AuthenticatedPayloadPrefix, StringComparison.Ordinal))
        {
            return DecryptAuthenticated(cipherText[AuthenticatedPayloadPrefix.Length..]);
        }

        return DecryptLegacy(cipherText);
    }

    private static string DecodePortable(string cipherText)
    {
        byte[] payload = Convert.FromBase64String(cipherText);
        return Encoding.UTF8.GetString(payload);
    }

    private static string DecryptLocal(string cipherText)
    {
        byte[] payload = Convert.FromBase64String(cipherText);
        if (payload.Length <= NonceBytes + TagBytes)
        {
            throw new CryptographicException("Invalid encrypted connection profile payload.");
        }

        byte[] nonce = new byte[NonceBytes];
        byte[] tag = new byte[TagBytes];
        byte[] cipherBytes = new byte[payload.Length - NonceBytes - TagBytes];
        Buffer.BlockCopy(payload, 0, nonce, 0, nonce.Length);
        Buffer.BlockCopy(payload, nonce.Length, tag, 0, tag.Length);
        Buffer.BlockCopy(payload, nonce.Length + tag.Length, cipherBytes, 0, cipherBytes.Length);

        byte[] plainBytes = new byte[cipherBytes.Length];
        using AesGcm aes = new(GetOrCreateLocalKey(), TagBytes);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static string DecryptAuthenticated(string cipherText)
    {
        byte[] payload = Convert.FromBase64String(cipherText);
        using Aes aes = Aes.Create();
        aes.Key = LegacyEncryptionKey;

        int ivLength = aes.BlockSize / 8;
        const int tagLength = 32;
        if (payload.Length <= ivLength + tagLength)
        {
            throw new CryptographicException("Invalid encrypted connection profile payload.");
        }

        int bodyLength = payload.Length - tagLength;
        byte[] body = new byte[bodyLength];
        byte[] tag = new byte[tagLength];
        Buffer.BlockCopy(payload, 0, body, 0, body.Length);
        Buffer.BlockCopy(payload, bodyLength, tag, 0, tag.Length);

        byte[] expectedTag = HMACSHA256.HashData(LegacyMacKey, body);
        if (!CryptographicOperations.FixedTimeEquals(tag, expectedTag))
        {
            throw new CryptographicException("Encrypted connection profile payload failed integrity validation.");
        }

        byte[] iv = new byte[ivLength];
        Buffer.BlockCopy(body, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using MemoryStream input = new(body, iv.Length, body.Length - iv.Length);
        using CryptoStream crypto = new(input, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using StreamReader reader = new(crypto, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string DecryptLegacy(string cipherText)
    {
        byte[] payload = Convert.FromBase64String(cipherText);
        using Aes aes = Aes.Create();
        aes.Key = SHA256.HashData(Salt);

        byte[] iv = new byte[aes.BlockSize / 8];
        if (payload.Length <= iv.Length)
        {
            throw new CryptographicException("Invalid legacy encrypted connection profile payload.");
        }

        Buffer.BlockCopy(payload, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using MemoryStream input = new(payload, iv.Length, payload.Length - iv.Length);
        using CryptoStream crypto = new(input, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using StreamReader reader = new(crypto, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] GetOrCreateLocalKey()
    {
        string path = GetLocalKeyPath();
        if (File.Exists(path))
        {
            byte[] existing = File.ReadAllBytes(path);
            if (existing.Length == LocalKeyBytes)
            {
                return existing;
            }
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        byte[] key = RandomNumberGenerator.GetBytes(LocalKeyBytes);
        File.WriteAllBytes(path, key);
        TryRestrictKeyFile(path);
        return key;
    }

    private static string GetLocalKeyPath()
    {
        string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        return Path.Combine(baseDirectory, "QueryPaw", "profile-key.bin");
    }

    private static void TryRestrictKeyFile(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // Best effort only. Some file systems do not support Unix mode changes.
        }
    }
}
