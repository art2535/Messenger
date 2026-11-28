using System.Security.Cryptography;

namespace Messenger.API.Extensions
{
    /// <summary>Создаёт общий локальный ключ разработки, не изменяя appsettings.</summary>
    public static class EncryptionKeyExtension
    {
        extension(WebApplicationBuilder builder)
        {
            public WebApplicationBuilder EnsureSharedDevelopmentEncryptionKey()
            {
                if (!builder.Environment.IsDevelopment() ||
                    !string.IsNullOrWhiteSpace(builder.Configuration["Encryption:MasterKeyBase64"]))
                    return builder;

                var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
                while (currentDir is not null && !currentDir.GetFiles("*.sln").Any())
                    currentDir = currentDir.Parent;

                if (currentDir is null)
                {
                    Console.WriteLine("[Encryption] Корень решения не найден. Задайте ключ через конфигурацию.");
                    return builder;
                }

                var sharedKeyPath = Path.Combine(currentDir.FullName, "shared-dev.key");
                var masterKey = File.Exists(sharedKeyPath)
                    ? File.ReadAllText(sharedKeyPath).Trim()
                    : Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

                if (!File.Exists(sharedKeyPath))
                {
                    File.WriteAllText(sharedKeyPath, masterKey);
                    Console.WriteLine("[Encryption] Создан локальный общий ключ разработки (shared-dev.key).");
                }

                builder.Configuration["Encryption:MasterKeyBase64"] = masterKey;
                return builder;
            }
        }
    }
}