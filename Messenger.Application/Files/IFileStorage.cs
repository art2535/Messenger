namespace Messenger.Application.Files;

/// <summary>
/// Абстракция файлового хранилища, не зависящая от способа физического хранения.
/// Ключ файла — только имя файла внутри одной из разрешённых областей.
/// </summary>
public interface IFileStorage
{
    Task<long> SaveAsync(
        FileStorageArea area,
        string fileName,
        Stream source,
        long maxSizeBytes,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        FileStorageArea area,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        FileStorageArea area,
        string fileName,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        FileStorageArea area,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<StoredFileInfo?> GetInfoAsync(
        FileStorageArea area,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredFileInfo>> ListAsync(
        FileStorageArea area,
        CancellationToken cancellationToken = default);
}
