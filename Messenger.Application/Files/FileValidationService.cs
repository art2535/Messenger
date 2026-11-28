namespace Messenger.Application.Files;

/// <summary>
/// Проверяет имя, размер, расширение и сигнатуру загружаемого файла.
/// MIME, переданный клиентом, не используется как источник истины.
/// </summary>
public sealed class FileValidationService : IFileValidationService
{
    private const long MaxAttachmentSize = 10 * 1024 * 1024;
    private const long MaxAvatarSize = 2 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".bmp"] = "image/bmp",
            [".avif"] = "image/avif",
            [".pdf"] = "application/pdf",
            [".txt"] = "text/plain",
            [".csv"] = "text/csv",
            [".zip"] = "application/zip",
            [".doc"] = "application/msword",
            [".xls"] = "application/vnd.ms-excel",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".mp3"] = "audio/mpeg",
            [".mp4"] = "video/mp4",
            [".wav"] = "audio/wav"
        };

    private static readonly HashSet<string> AvatarExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

    public async Task<ValidatedFile> ValidateAsync(
        IUploadedFile file,
        FileUploadPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var maxSize = purpose switch
        {
            FileUploadPurpose.Attachment => MaxAttachmentSize,
            FileUploadPurpose.Avatar => MaxAvatarSize,
            _ => throw new ArgumentOutOfRangeException(nameof(purpose))
        };

        if (file.Length <= 0 || file.Length > maxSize)
            throw new ArgumentException($"Размер файла должен быть больше 0 и не превышать {maxSize / (1024 * 1024)} МБ");

        var normalizedName = (file.FileName ?? string.Empty).Replace('\\', '/');
        var fileName = Path.GetFileName(normalizedName).Trim();
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Length > 255
            || fileName.Any(char.IsControl))
            throw new ArgumentException("Некорректное имя файла");

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!ContentTypes.TryGetValue(extension, out var contentType))
            throw new ArgumentException("Неподдерживаемый формат файла");

        if (purpose == FileUploadPurpose.Avatar && !AvatarExtensions.Contains(extension))
            throw new ArgumentException("Аватар должен быть изображением JPG, PNG, GIF или WEBP");

        var signature = new byte[16];
        var bytesRead = 0;
        await using (var stream = file.OpenReadStream())
        {
            while (bytesRead < signature.Length)
            {
                var read = await stream.ReadAsync(signature.AsMemory(bytesRead), cancellationToken);
                if (read == 0)
                    break;
                bytesRead += read;
            }
        }

        if (!MatchesSignature(signature.AsSpan(0, bytesRead), extension))
            throw new ArgumentException("Содержимое файла не соответствует заявленному формату");

        return new ValidatedFile(fileName, extension, contentType, file.Length);
    }

    private static bool MatchesSignature(ReadOnlySpan<byte> bytes, string extension)
    {
        return extension switch
        {
            ".jpg" or ".jpeg" => bytes.Length >= 3
                && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            ".png" => bytes.Length >= 8
                && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".gif" => bytes.Length >= 6
                && bytes[..3].SequenceEqual("GIF"u8)
                && (bytes[3] == (byte)'8')
                && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9')
                && bytes[5] == (byte)'a',
            ".webp" => bytes.Length >= 12
                && bytes[..4].SequenceEqual("RIFF"u8)
                && bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
            ".bmp" => bytes.Length >= 2 && bytes[..2].SequenceEqual("BM"u8),
            ".avif" => bytes.Length >= 12
                && bytes.Slice(4, 4).SequenceEqual("ftyp"u8)
                && (bytes.Slice(8, 4).SequenceEqual("avif"u8)
                    || bytes.Slice(8, 4).SequenceEqual("avis"u8)),
            ".pdf" => bytes.Length >= 5 && bytes[..5].SequenceEqual("%PDF-"u8),
            ".zip" or ".docx" or ".xlsx" or ".pptx" => bytes.Length >= 4
                && bytes[0] == 0x50 && bytes[1] == 0x4B
                && bytes[2] is 0x03 or 0x05 or 0x07
                && bytes[3] is 0x04 or 0x06 or 0x08,
            ".doc" or ".xls" or ".ppt" => bytes.Length >= 8
                && bytes[..8].SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }),
            ".mp3" => bytes.Length >= 3
                && (bytes[..3].SequenceEqual("ID3"u8)
                    || (bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0)),
            ".mp4" => bytes.Length >= 8 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8),
            ".wav" => bytes.Length >= 12
                && bytes[..4].SequenceEqual("RIFF"u8)
                && bytes.Slice(8, 4).SequenceEqual("WAVE"u8),
            ".txt" or ".csv" => bytes.Length > 0 && !bytes.Contains((byte)0),
            _ => false
        };
    }
}
