namespace Messenger.Application.Files;

/// <summary>
/// Метаданные файла, найденного в хранилище.
/// </summary>
public sealed record StoredFileInfo(
    string FileName,
    long Length,
    DateTime LastModifiedUtc);
