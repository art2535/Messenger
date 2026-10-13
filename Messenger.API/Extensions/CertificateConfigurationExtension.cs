using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace Messenger.API.Extensions
{
    public static class CertificateConfigurationExtension
    {
        private const string LinuxCertFilePath = "/usr/local/share/ca-certificates/aspnetcore.crt";
        private const string LinuxSetupScriptPath = "/usr/local/bin/setup-aspnet-cert.sh";
        private const string TempPemPath = "/tmp/aspnetcore.crt";


        extension(WebApplicationBuilder builder)
        {
            public WebApplicationBuilder ConfigureDevelopmentCertificate()
            {
                if (OperatingSystem.IsLinux())
                {
                    ConfigureLinuxCertificate();
                }
                else
                {
                    ConfigureWindowsCertificate(builder);
                }

                return builder;
            }
        }

        private static void ConfigureWindowsCertificate(WebApplicationBuilder builder)
        {
            var storeName = Enum.Parse<StoreName>(
                builder.Configuration["CertificateProfiles:Windows:StoreName"] ?? "My");
            var storeLocation = Enum.Parse<StoreLocation>(
                builder.Configuration["CertificateProfiles:Windows:StoreLocation"] ?? "LocalMachine");
            var subject = builder.Configuration["CertificateProfiles:Windows:Subject"] ?? "localhost";

            Console.WriteLine($"[Kestrel Config] Ищем сертификат в хранилище: {storeLocation}\\{storeName} с Subject='{subject}'");

            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.ConfigureHttpsDefaults(listenOptions =>
                {
                    using var store = new X509Store(storeName, storeLocation);
                    store.Open(OpenFlags.ReadOnly);

                    var certs = store.Certificates.Find(X509FindType.FindBySubjectName, subject, validOnly: false);

                    if (certs.Count > 0)
                    {
                        listenOptions.ServerCertificate = certs
                            .OfType<X509Certificate2>()
                            .OrderByDescending(c => c.NotAfter)
                            .First();

                        Console.WriteLine($"[Kestrel Config] Найден сертификат: {listenOptions.ServerCertificate.Subject} (Thumbprint: {listenOptions.ServerCertificate.Thumbprint})");
                    }
                    else
                    {
                        Console.WriteLine($"[Kestrel Config] Сертификат с Subject='{subject}' не найден. Kestrel использует встроенный dev-сертификат.");
                    }
                });
            });
        }

        private static void ConfigureLinuxCertificate()
        {
            Console.WriteLine("[Kestrel Config] Linux/WSL: Проверяем наличие ASP.NET Core Developer Certificate...");

            EnsureDevCertificateExists();
            EnsureCertificateTrustedBySystem();

            Console.WriteLine("[Kestrel Config] Linux/WSL: Используем автоматический ASP.NET Core Developer Certificate.");
        }

        private static void EnsureDevCertificateExists()
        {
            if (RunDotnetCommand("dev-certs https --check", out _, out _))
            {
                Console.WriteLine("[Cert Setup] ✓ Dev-сертификат уже существует.");
                return;
            }

            Console.WriteLine("[Cert Setup] Dev-сертификат не найден. Создаем новый...");
            if (!RunDotnetCommand("dev-certs https", out _, out var error))
            {
                Console.WriteLine($"[Cert Setup] ✗ Ошибка создания сертификата: {error}");
                return;
            }

            Console.WriteLine("[Cert Setup] ✓ Dev-сертификат успешно создан.");
        }

        private static void EnsureCertificateTrustedBySystem()
        {
            if (File.Exists(LinuxCertFilePath))
            {
                Console.WriteLine($"[Cert Setup] ✓ Сертификат уже доверен системой: {LinuxCertFilePath}");
                return;
            }

            Console.WriteLine("[Cert Setup] Экспортируем сертификат в PEM-формат...");
            if (!RunDotnetCommand($"dev-certs https --export-path {TempPemPath} --no-password --format pem", out _, out var exportError))
            {
                Console.WriteLine($"[Cert Setup] Ошибка экспорта: {exportError}");
                return;
            }

            if (!File.Exists(TempPemPath))
            {
                Console.WriteLine($"[Cert Setup] Файл сертификата не создан: {TempPemPath}");
                return;
            }

            if (!File.Exists(LinuxSetupScriptPath))
            {
                Console.WriteLine($"[Cert Setup] ⚠️  Скрипт установки не найден: {LinuxSetupScriptPath}");
                Console.WriteLine("[Cert Setup] Создайте скрипт и настройте sudoers (см. документацию).");
                return;
            }

            Console.WriteLine("[Cert Setup] Запускаем скрипт установки сертификата через sudo...");
            if (RunCommand("sudo", LinuxSetupScriptPath, out var output, out var scriptError))
            {
                Console.WriteLine($"[Cert Setup] {output.Trim()}");
                Console.WriteLine("[Cert Setup] ✓ Сертификат успешно установлен и доверен системой.");
            }
            else
            {
                Console.WriteLine($"[Cert Setup] ✗ Ошибка выполнения скрипта: {scriptError}");
                Console.WriteLine("[Cert Setup] Убедитесь, что скрипт добавлен в sudoers с NOPASSWD.");
            }
        }

        private static bool RunDotnetCommand(string arguments, out string output, out string error)
        {
            return RunCommand("dotnet", arguments, out output, out error);
        }

        private static bool RunCommand(string fileName, string arguments, out string output, out string error)
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.Start();
                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                output = string.Empty;
                error = ex.Message;
                return false;
            }
        }
    }
}