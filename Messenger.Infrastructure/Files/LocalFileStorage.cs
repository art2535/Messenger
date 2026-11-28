using Messenger.Application.Files;

namespace Messenger.Infrastructure.Files;

/// <summary>
/// Хранилище файлов в выделенных каталогах под корневой директорией.
/// Не публикует файлы самостоятельно: доступ к вложениям контролирует API.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;
    private readonly StringComparison _pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public LocalFileStorage(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<long> SaveAsync(
        FileStorageArea area,
        string fileName,
        Stream source,
        long maxSizeBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maxSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSizeBytes));

        var path = GetFilePath(area, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var fileCreated = false;
        try
        {
            await using var destination = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            fileCreated = true;

            var buffer = new byte[81920];
            long totalBytes = 0;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > maxSizeBytes)
                    throw new InvalidDataException("Размер файла превышает допустимый предел");

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }

            if (totalBytes == 0)
                throw new InvalidDataException("Нельзя сохранить пустой файл");

            await destination.FlushAsync(cancellationToken);
            return totalBytes;
        }
        catch
        {
            if (fileCreated)
                TryDelete(path);
            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(
        FileStorageArea area,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetFilePath(area, fileName);
        if (!File.Exists(path))
            return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> ExistsAsync(
        FileStorageArea area,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(GetFilePath(area, fileName)));
    }

    public Task DeleteAsync(
        FileStorageArea area,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetFilePath(area, fileName);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    public Task<StoredFileInfo?> GetInfoAsync(
        FileStorageArea area,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetFilePath(area, fileName);
        if (!File.Exists(path))
            return Task.FromResult<StoredFileInfo?>(null);

        var info = new FileInfo(path);
        return Task.FromResult<StoredFileInfo?>(
            new StoredFileInfo(info.Name, info.Length, info.LastWriteTimeUtc));
    }

    public Task<IReadOnlyList<StoredFileInfo>> ListAsync(
        FileStorageArea area,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = GetAreaPath(area);
        if (!Directory.Exists(directory))
            return Task.FromResult<IReadOnlyList<StoredFileInfo>>([]);

        var files = new List<StoredFileInfo>();
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            files.Add(new StoredFileInfo(info.Name, info.Length, info.LastWriteTimeUtc));
        }

        return Task.FromResult<IReadOnlyList<StoredFileInfo>>(files);
    }

    private string GetFilePath(FileStorageArea area, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName is "." or ".."
            || Path.IsPathRooted(fileName)
            || fileName.Contains('/')
            || fileName.Contains('\\')
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Некорректное имя файла", nameof(fileName));
        }

        var directory = GetAreaPath(area);
        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        var prefix = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;

        if (!path.StartsWith(prefix, _pathComparison))
            throw new ArgumentException("Путь файла выходит за пределы хранилища", nameof(fileName));

        return path;
    }

    private string GetAreaPath(FileStorageArea area)
    {
        var directoryName = area switch
        {
            FileStorageArea.Uploads => "uploads",
            FileStorageArea.Avatars => "avatars",
            _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Неизвестная область хранилища")
        };

        var directory = Path.GetFullPath(Path.Combine(_rootPath, directoryName));
        var prefix = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        if (!directory.StartsWith(prefix, _pathComparison))
            throw new InvalidOperationException("Каталог выходит за пределы корня хранилища");

        return directory;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Исходная ошибка записи важнее ошибки удаления частичного файла.
        }
        catch (UnauthorizedAccessException)
        {
            // Исходная ошибка записи важнее ошибки удаления частичного файла.
        }
    }
}
