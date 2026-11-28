using Messenger.Application.Files;
using Messenger.Tests.Shared.TestDoubles;
using Xunit;

namespace Messenger.Tests.Application.Files.Validation;

public sealed class FileValidationServiceTests
{
    private readonly FileValidationService _validator = new();

    [Fact]
    public async Task ValidateAsync_UsesDetectedMimeInsteadOfClientContentType()
    {
        var file = new FakeUploadedFile(
            "photo.png",
            "text/plain",
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var result = await _validator.ValidateAsync(file, FileUploadPurpose.Attachment);

        Assert.Equal("photo.png", result.FileName);
        Assert.Equal(".png", result.Extension);
        Assert.Equal("image/png", result.ContentType);
    }

    [Fact]
    public async Task ValidateAsync_RejectsContentThatDoesNotMatchExtension()
    {
        var file = new FakeUploadedFile("photo.png", "image/png", "%PDF-1.7"u8.ToArray());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _validator.ValidateAsync(file, FileUploadPurpose.Attachment));
    }

    [Fact]
    public async Task ValidateAsync_RejectsUnsupportedExtension()
    {
        var file = new FakeUploadedFile("payload.exe", "application/octet-stream", [0x4D, 0x5A]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _validator.ValidateAsync(file, FileUploadPurpose.Attachment));
    }

    [Fact]
    public async Task ValidateAsync_RejectsFilesLargerThanAttachmentLimit()
    {
        var file = new FakeUploadedFile("large.pdf", "application/pdf", new byte[10 * 1024 * 1024 + 1]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _validator.ValidateAsync(file, FileUploadPurpose.Attachment));
    }

    [Fact]
    public async Task ValidateAsync_AvatarPurposeRejectsDocuments()
    {
        var file = new FakeUploadedFile("document.pdf", "application/pdf", "%PDF-1.7"u8.ToArray());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _validator.ValidateAsync(file, FileUploadPurpose.Avatar));
    }

    [Fact]
    public async Task ValidateAsync_SanitizesClientPathFromDisplayName()
    {
        var file = new FakeUploadedFile(
            "../photo.png",
            "application/octet-stream",
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var result = await _validator.ValidateAsync(file, FileUploadPurpose.Avatar);

        Assert.Equal("photo.png", result.FileName);
    }
    [Fact]
    public async Task ValidateAsync_RejectsEmptyFiles()
    {
        var file = new FakeUploadedFile("empty.png", "image/png", []);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _validator.ValidateAsync(file, FileUploadPurpose.Attachment));
    }

    [Fact]
    public async Task ValidateAsync_RejectsAvatarLargerThanAvatarLimit()
    {
        var file = new FakeUploadedFile(
            "large.png",
            "image/png",
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[2 * 1024 * 1024]]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _validator.ValidateAsync(file, FileUploadPurpose.Avatar));
    }

    [Fact]
    public async Task ValidateAsync_RejectsControlCharactersInFileName()
    {
        var file = new FakeUploadedFile(
            "photo\n.png",
            "image/png",
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _validator.ValidateAsync(file, FileUploadPurpose.Attachment));
    }

}
