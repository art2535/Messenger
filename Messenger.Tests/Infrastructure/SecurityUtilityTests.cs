using System.Security.Cryptography;
using Messenger.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Messenger.Tests.Infrastructure;

public sealed class SecurityUtilityTests
{
    private static IOptions<AesGcmEncryptionOptions> ValidEncryptionOptions() =>
        Options.Create(new AesGcmEncryptionOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray())
        });

    [Fact]
    public void Encryption_RoundTripsUnicodeAndEmptyValues()
    {
        var service = new AesGcmEncryptionService(ValidEncryptionOptions());
        const string plainText = "Привет, Messenger 🔐";
        var encrypted = service.Encrypt(plainText);

        Assert.NotEqual(plainText, encrypted);
        Assert.Equal(plainText, service.Decrypt(encrypted));
        Assert.Equal(string.Empty, service.Encrypt(string.Empty));
        Assert.Equal(string.Empty, service.Decrypt(string.Empty));
        Assert.Equal(string.Empty, service.TryDecryptSafe(null));
    }

    [Fact]
    public void Encryption_UsesRandomNonceForSamePlainText()
    {
        var service = new AesGcmEncryptionService(ValidEncryptionOptions());
        var first = service.Encrypt("same");
        var second = service.Encrypt("same");

        Assert.NotEqual(first, second);
        Assert.Equal("same", service.Decrypt(first));
        Assert.Equal("same", service.Decrypt(second));
    }

    [Theory]
    [InlineData("not-base64!")]
    [InlineData("AQID")]
    public void Decrypt_RejectsMalformedCiphertext(string cipherText)
    {
        var service = new AesGcmEncryptionService(ValidEncryptionOptions());
        Assert.Throws<CryptographicException>(() => service.Decrypt(cipherText));
    }

    [Fact]
    public void Decrypt_RejectsTamperedCiphertext_AndSafeDecryptReturnsFallback()
    {
        var service = new AesGcmEncryptionService(ValidEncryptionOptions());
        var bytes = Convert.FromBase64String(service.Encrypt("secret"));
        bytes[^1] ^= 0x40;
        var tampered = Convert.ToBase64String(bytes);

        Assert.Throws<CryptographicException>(() => service.Decrypt(tampered));
        Assert.Equal("[Сообщение зашифровано или повреждено]", service.TryDecryptSafe(tampered));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("%%%")]
    [InlineData("AQID")]
    public void Encryption_RejectsInvalidMasterKey(string? key)
    {
        Assert.Throws<ArgumentException>(() => new AesGcmEncryptionService(
            Options.Create(new AesGcmEncryptionOptions { MasterKeyBase64 = key! })));
    }

    [Theory]
    [InlineData("Abcd123!")]
    [InlineData("StrongPass9#")]
    public void ValidatePassword_AcceptsValidPassword(string password) =>
        Assert.Equal(password, ValidationService.ValidatePassword(password));

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("lowercase1!")]
    [InlineData("UPPERCASE1!")]
    [InlineData("NoDigits!!")]
    [InlineData("NoSpecial123")]
    [InlineData("ThisPasswordIsMuchTooLong123!")]
    public void ValidatePassword_RejectsInvalidPassword(string password) =>
        Assert.Throws<Exception>(() => ValidationService.ValidatePassword(password));

    [Theory]
    [InlineData("person@example.com")]
    [InlineData("first.last+tag@example.co.uk")]
    public void ValidateEmail_AcceptsValidEmail(string email) =>
        Assert.Equal(email, ValidationService.ValidateEmail(email));

    [Theory]
    [InlineData("invalid")]
    [InlineData("person@localhost")]
    [InlineData("@example.com")]
    public void ValidateEmail_RejectsInvalidEmail(string email) =>
        Assert.Throws<FormatException>(() => ValidationService.ValidateEmail(email));

    [Fact]
    public void PasswordHash_IsNotPlainTextAndVerifiesOnlyOriginalPassword()
    {
        var hash = ValidationService.HashPassword("StrongPass9!");
        Assert.NotEqual("StrongPass9!", hash);
        Assert.True(ValidationService.VerifyPassword("StrongPass9!", hash));
        Assert.False(ValidationService.VerifyPassword("DifferentPass9!", hash));
    }

    [Fact]
    public void ValidateBirthdate_RejectsFutureDate() =>
        Assert.Throws<Exception>(() => ValidationService.ValidateBirthdate(DateTime.UtcNow.AddDays(1)));
}
