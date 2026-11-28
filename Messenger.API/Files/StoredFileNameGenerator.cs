namespace Messenger.API.Files;

/// <summary>
/// Создаёт серверные имена вложений и проверяет их привязку к владельцу.
/// Расширение должно поступать из результата серверной валидации файла.
/// </summary>
public static class StoredFileNameGenerator
{
    public static string Create(Guid ownerId, string validatedExtension)
    {
        if (ownerId == Guid.Empty)
            throw new ArgumentException("Не указан владелец файла", nameof(ownerId));

        ArgumentException.ThrowIfNullOrWhiteSpace(validatedExtension);
        if (validatedExtension.Length is < 2 or > 16 || validatedExtension[0] != '.')
            throw new ArgumentException("Некорректное расширение файла", nameof(validatedExtension));

        foreach (var character in validatedExtension.AsSpan(1))
        {
            var isAsciiLetter = character is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
            var isDigit = character is >= '0' and <= '9';
            if (!isAsciiLetter && !isDigit)
                throw new ArgumentException("Некорректное расширение файла", nameof(validatedExtension));
        }

        return $"{ownerId:N}_{Guid.NewGuid():N}{validatedExtension.ToLowerInvariant()}";
    }

    public static bool IsOwnedBy(Guid ownerId, string? storedFileName)
    {
        if (ownerId == Guid.Empty || string.IsNullOrWhiteSpace(storedFileName))
            return false;

        return storedFileName.StartsWith($"{ownerId:N}_", StringComparison.OrdinalIgnoreCase);
    }
}
