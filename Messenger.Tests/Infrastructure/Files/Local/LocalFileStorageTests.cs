using Messenger.Application.Files;
using Messenger.Infrastructure.Files;
using Xunit;

namespace Messenger.Tests.Infrastructure.Files.Local;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(), "Messenger.Tests", Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _storage = new LocalFileStorage(_rootPath);
    }

    [Fact]
    public async Task SaveAsync_StoresFileAndReturnsActualLength()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };

        var length = await _storage.SaveAsync(
            FileStorageArea.Uploads,
            "message.bin",
            new MemoryStream(content),
            maxSizeBytes: 10);

        Assert.Equal(content.Length, length);
        Assert.True(await _storage.ExistsAsync(FileStorageArea.Uploads, "message.bin"));

        var info = await _storage.GetInfoAsync(FileStorageArea.Uploads, "message.bin");
        Assert.NotNull(info);
        Assert.Equal(content.Length, info.Length);

        await using var stored = await _storage.OpenReadAsync(FileStorageArea.Uploads, "message.bin");
        Assert.NotNull(stored);
        using var result = new MemoryStream();
        await stored.CopyToAsync(result);
        Assert.Equal(content, result.ToArray());
    }

    [Fact]
    public async Task SaveAsync_RejectsPathTraversal()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SaveAsync(
            FileStorageArea.Uploads,
            "../outside.txt",
            new MemoryStream([1]),
            maxSizeBytes: 10));
    }

    [Fact]
    public async Task SaveAsync_RejectsOversizedStreamAndRemovesPartialFile()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => _storage.SaveAsync(
            FileStorageArea.Uploads,
            "oversized.bin",
            new MemoryStream(new byte[11]),
            maxSizeBytes: 10));

        Assert.False(await _storage.ExistsAsync(FileStorageArea.Uploads, "oversized.bin"));
        Assert.Empty(await _storage.ListAsync(FileStorageArea.Uploads));
    }

    [Fact]
    public async Task SaveAsync_DoesNotDeleteExistingFileWhenNameCollides()
    {
        var original = new byte[] { 1, 2, 3 };
        await _storage.SaveAsync(
            FileStorageArea.Uploads, "collision.bin", new MemoryStream(original), 10);

        await Assert.ThrowsAsync<IOException>(() => _storage.SaveAsync(
            FileStorageArea.Uploads, "collision.bin", new MemoryStream([9, 9, 9]), 10));

        await using var stored = await _storage.OpenReadAsync(FileStorageArea.Uploads, "collision.bin");
        Assert.NotNull(stored);
        using var result = new MemoryStream();
        await stored.CopyToAsync(result);
        Assert.Equal(original, result.ToArray());
    }

    [Fact]
    public async Task StorageAreas_AreIsolated()
    {
        await _storage.SaveAsync(FileStorageArea.Uploads, "same.png", new MemoryStream([1]), 10);
        await _storage.SaveAsync(FileStorageArea.Avatars, "same.png", new MemoryStream([2]), 10);

        Assert.True(await _storage.ExistsAsync(FileStorageArea.Uploads, "same.png"));
        Assert.True(await _storage.ExistsAsync(FileStorageArea.Avatars, "same.png"));

        await _storage.DeleteAsync(FileStorageArea.Avatars, "same.png");

        Assert.True(await _storage.ExistsAsync(FileStorageArea.Uploads, "same.png"));
        Assert.False(await _storage.ExistsAsync(FileStorageArea.Avatars, "same.png"));
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsNullForMissingFile()
    {
        var stream = await _storage.OpenReadAsync(FileStorageArea.Uploads, "missing.png");

        Assert.Null(stream);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
            Directory.Delete(_rootPath, recursive: true);
    }
    [Fact]
    public async Task SaveAsync_RejectsEmptyStream()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => _storage.SaveAsync(
            FileStorageArea.Uploads,
            "empty.bin",
            Stream.Null,
            maxSizeBytes: 10));

        Assert.False(await _storage.ExistsAsync(FileStorageArea.Uploads, "empty.bin"));
    }

    [Fact]
    public async Task SaveAsync_ObservesCancellationBeforeWriting()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _storage.SaveAsync(
            FileStorageArea.Uploads,
            "cancelled.bin",
            new MemoryStream([1, 2, 3]),
            maxSizeBytes: 10,
            cancellationToken: cancellation.Token));

        Assert.False(await _storage.ExistsAsync(FileStorageArea.Uploads, "cancelled.bin"));
    }

}
