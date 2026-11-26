using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Messenger.Core.Interfaces;

namespace Messenger.Infrastructure.Services
{
    public class AesGcmEncryptionOptions
    {
        public string MasterKeyBase64 { get; set; } = null!;
    }

    public class AesGcmEncryptionService : IEncryptionService
    {
        private readonly byte[] _masterKey;

        public AesGcmEncryptionService(IOptions<AesGcmEncryptionOptions> options)
        {
            if (options?.Value == null || string.IsNullOrWhiteSpace(options.Value.MasterKeyBase64))
            {
                throw new ArgumentException("Encryption:MasterKeyBase64 не задан в конфигурации");
            }

            try
            {
                _masterKey = Convert.FromBase64String(options.Value.MasterKeyBase64.Trim());
            }
            catch (FormatException ex)
            {
                throw new ArgumentException("Некорректный формат MasterKeyBase64 (должен быть валидный base64)", ex);
            }

            if (_masterKey.Length != 32)
            {
                throw new ArgumentException($"Master key должен быть ровно 32 байта (AES-256). Текущая длина: {_masterKey.Length}");
            }
        }

        public string Encrypt(string? plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText))
                return plainText ?? "";

            var plaintextBytes = Encoding.UTF8.GetBytes(plainText);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[16];

            using var aesGcm = new AesGcm(_masterKey, 16);
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            var combined = new byte[12 + 16 + ciphertext.Length];
            nonce.CopyTo(combined, 0);
            tag.CopyTo(combined, 12);
            ciphertext.CopyTo(combined, 28);

            return Convert.ToBase64String(combined);
        }

        public string Decrypt(string? cipherText)
        {
            if (string.IsNullOrWhiteSpace(cipherText))
                return cipherText ?? "";

            byte[] combined;
            try
            {
                combined = Convert.FromBase64String(cipherText);
            }
            catch (FormatException)
            {
                throw new CryptographicException("Некорректный формат зашифрованных данных (невалидный base64)");
            }

            if (combined.Length < 12 + 16)
            {
                throw new CryptographicException("Слишком короткие зашифрованные данные");
            }

            var nonce = combined.AsSpan(0, 12);
            var tag = combined.AsSpan(12, 16);
            var ciphertext = combined.AsSpan(28);

            var plaintextBytes = new byte[ciphertext.Length];

            try
            {
                using var aesGcm = new AesGcm(_masterKey, 16);
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException("Не удалось расшифровать сообщение. Возможно, неверный ключ или повреждены данные.", ex);
            }

            return Encoding.UTF8.GetString(plaintextBytes);
        }

        public string TryDecryptSafe(string? encryptedText)
        {
            if (string.IsNullOrWhiteSpace(encryptedText))
                return "";

            try
            {
                return Decrypt(encryptedText);
            }
            catch
            {
                return "[Сообщение зашифровано или повреждено]";
            }
        }
    }
}
