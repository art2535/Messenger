using System.Text.RegularExpressions;
using Bcrypt = BCrypt.Net.BCrypt;

namespace Messenger.Infrastructure.Services
{
    public static class ValidationService
    {
        public static string HashPassword(string password)
        {
            return Bcrypt.HashPassword(password);
        }

        public static bool VerifyPassword(string password, string passwordHash)
        {
            return Bcrypt.Verify(password, passwordHash);
        }

        public static string ValidatePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new Exception("Пароль не должен быть пустым");

            if (!Regex.IsMatch(password, @"^.{8,20}$"))
                throw new Exception("Пароль должен содержать от 8 до 20 символов");

            if (!Regex.IsMatch(password, @"[A-Z]"))
                throw new Exception("Пароль должен содержать хотя бы одну заглавную букву (A-Z)");

            if (!Regex.IsMatch(password, @"[a-z]"))
                throw new Exception("Пароль должен содержать хотя бы одну строчную букву (a-z)");

            if (!Regex.IsMatch(password, @"\d"))
                throw new Exception("Пароль должен содержать хотя бы одну цифру (0-9)");

            if (!Regex.IsMatch(password, @"[^\da-zA-Z]"))
                throw new Exception("Пароль должен содержать хотя бы один специальный символ (например: !@#$%^&*)");

            return password;
        }

        public static DateOnly ValidateBirthdate(DateTime dateTime)
        {
            if (dateTime > DateTime.UtcNow)
            {
                throw new Exception($"Дата рождения не может быть позднее {DateTime.UtcNow}");
            }
            return DateOnly.FromDateTime(dateTime);
        }

        public static string ValidateEmail(string email)
        {
            if (!Regex.IsMatch(email, @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$"))
            {
                throw new FormatException("Email не соответствует формату");
            }
            return email;
        }
    }
}
