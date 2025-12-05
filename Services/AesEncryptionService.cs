using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

public class AesEncryptionService
{
    private readonly byte[] _key;
    private readonly IServiceScopeFactory _scopeFactory;

    public AesEncryptionService(IConfiguration configuration, IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;

        var keyBase64 = configuration["Encryption:Key"];
        if (string.IsNullOrWhiteSpace(keyBase64))
            throw new InvalidOperationException("Encryption key not found in configuration.");

        _key = Convert.FromBase64String(keyBase64);
        if (_key.Length != 32)
            throw new InvalidOperationException("Encryption key must be 32 bytes (256 bits) for AES-256.");
    }

    public string Encrypt(string plainText)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.GenerateIV();
            var iv = aes.IV;

            using var encryptor = aes.CreateEncryptor(aes.Key, iv);
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            // Prepend IV to cipher text
            var result = new byte[iv.Length + cipherBytes.Length];
            Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
            Buffer.BlockCopy(cipherBytes, 0, result, iv.Length, cipherBytes.Length);

            return Convert.ToBase64String(result);
        }
        catch (Exception ex)
        {
            LogErrorAsync("Encryption failed", ex.StackTrace, nameof(AesEncryptionService), ex.Message);
            throw;
        }
    }

    public string Decrypt(string cipherText)
    {
        try
        {
            var fullCipher = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.Key = _key;

            // Extract IV
            var iv = new byte[aes.BlockSize / 8];
            var cipherBytes = new byte[fullCipher.Length - iv.Length];
            Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(fullCipher, iv.Length, cipherBytes, 0, cipherBytes.Length);

            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            LogErrorAsync("Decryption failed", ex.StackTrace, nameof(AesEncryptionService), ex.Message);
            throw;
        }
    }

    private void LogErrorAsync(string message, string? stackTrace, string? source, string? additionalData)
    {
        // Create a scope to resolve the scoped ErrorLogService
        using var scope = _scopeFactory.CreateScope();
        var errorLogService = scope.ServiceProvider.GetRequiredService<ErrorLogService>();
        _ = errorLogService.LogErrorAsync(message, stackTrace, source, additionalData);
    }
}
