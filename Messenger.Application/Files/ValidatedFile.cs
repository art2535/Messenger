namespace Messenger.Application.Files;

/// <summary>
/// Проверенные метаданные входящего файла. MIME определяется по содержимому,
/// а не принимается на доверии из HTTP-запроса.
/// </summary>
public sealed record ValidatedFile(
    string FileName,
    string Extension,
    string ContentType,
    long Length);
