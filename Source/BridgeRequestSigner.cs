using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AspectDiscordBot;

internal sealed class BridgeRequestSigner : IDisposable
{
    private readonly RSA? _rsa;
    private readonly string? _keyId;
    private readonly string? _apiKey;

    public BridgeRequestSigner(ServerConfig config)
    {
        _keyId = string.IsNullOrWhiteSpace(config.KeyId) ? "aspect-bot" : config.KeyId.Trim();
        _apiKey = config.ApiKey;

        string keyText = config.SshPrivateKey;
        if (string.IsNullOrWhiteSpace(keyText) || keyText.EndsWith(".key", StringComparison.OrdinalIgnoreCase) || keyText.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
        {
            string keyPath = string.IsNullOrWhiteSpace(config.SshPrivateKeyPath) ? "aspect_bridge.key" : config.SshPrivateKeyPath;
            if (!Path.IsPathRooted(keyPath))
            {
                keyPath = Path.Combine(AppContext.BaseDirectory, keyPath);
            }

            if (File.Exists(keyPath))
            {
                try
                {
                    keyText = File.ReadAllText(keyPath, Encoding.UTF8).Trim();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Signer] Не удалось прочитать приватный ключ '{keyPath}': {ex.Message}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(keyText))
        {
            try
            {
                _rsa = LoadPrivateKey(keyText);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Signer] Ошибка загрузки приватного ключа: {ex.Message}");
            }
        }
    }

    public bool HasPrivateKey => _rsa != null;

    public void ApplyAuthHeaders(HttpRequestMessage request, byte[]? bodyBytes)
    {
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Remove("X-Aspect-Key");
            request.Headers.Add("X-Aspect-Key", _apiKey);
        }

        if (_rsa == null)
            return;

        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string method = request.Method.Method.ToUpperInvariant();
        string pathAndQuery = request.RequestUri?.PathAndQuery ?? "/";
        string bodyHashHex = ComputeSha256Hex(bodyBytes);

        string payloadToSign = $"{timestamp}\n{method}\n{pathAndQuery}\n{bodyHashHex}";
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadToSign);
        byte[] signatureBytes = _rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        string signatureBase64 = Convert.ToBase64String(signatureBytes);

        request.Headers.Remove("X-Aspect-Timestamp");
        request.Headers.Add("X-Aspect-Timestamp", timestamp);

        request.Headers.Remove("X-Aspect-Signature");
        request.Headers.Add("X-Aspect-Signature", signatureBase64);

        if (!string.IsNullOrWhiteSpace(_keyId))
        {
            request.Headers.Remove("X-Aspect-Key-Id");
            request.Headers.Add("X-Aspect-Key-Id", _keyId);
        }
    }

    private static RSA LoadPrivateKey(string text)
    {
        string trimmed = text.Trim();
        var rsa = RSA.Create();

        if (trimmed.StartsWith("<RSAKeyValue>", StringComparison.OrdinalIgnoreCase))
        {
            rsa.FromXmlString(trimmed);
            return rsa;
        }

        if (trimmed.Contains("-----BEGIN"))
        {
            rsa.ImportFromPem(trimmed);
            return rsa;
        }

        try
        {
            byte[] raw = Convert.FromBase64String(trimmed);
            rsa.ImportPkcs8PrivateKey(raw, out _);
            return rsa;
        }
        catch
        {
            rsa.ImportFromPem(trimmed);
            return rsa;
        }
    }

    public static (string PrivateKeyPem, string PublicKeyOpenSsh) GenerateKeyPair()
    {
        using var rsa = RSA.Create(2048);
        string privateKeyPem = rsa.ExportPkcs8PrivateKeyPem();

        var rsaParams = rsa.ExportParameters(false);
        byte[] openSshBytes = EncodeOpenSshPublicKey(rsaParams);
        string publicKeyOpenSsh = "ssh-rsa " + Convert.ToBase64String(openSshBytes) + " aspect-discord-bot";

        return (privateKeyPem, publicKeyOpenSsh);
    }

    private static byte[] EncodeOpenSshPublicKey(RSAParameters rsaParams)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        byte[] typeBytes = Encoding.ASCII.GetBytes("ssh-rsa");
        WriteIntBigEndian(writer, typeBytes.Length);
        writer.Write(typeBytes);

        WriteIntBigEndian(writer, rsaParams.Exponent!.Length);
        writer.Write(rsaParams.Exponent!);

        byte[] mod = rsaParams.Modulus!;
        if ((mod[0] & 0x80) != 0)
        {
            WriteIntBigEndian(writer, mod.Length + 1);
            writer.Write((byte)0x00);
            writer.Write(mod);
        }
        else
        {
            WriteIntBigEndian(writer, mod.Length);
            writer.Write(mod);
        }

        return ms.ToArray();
    }

    private static void WriteIntBigEndian(BinaryWriter writer, int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        writer.Write(bytes);
    }

    private static string ComputeSha256Hex(byte[]? data)
    {
        if (data == null || data.Length == 0)
            return "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(data);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public void Dispose() => _rsa?.Dispose();
}
