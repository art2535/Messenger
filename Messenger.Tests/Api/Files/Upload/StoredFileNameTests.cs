using Messenger.API.Files;
using Xunit;

namespace Messenger.Tests.Api.Files.Upload;

public sealed class StoredFileNameTests
{
    [Fact]
    public void Create_EmbedsOwnerIdAndKeepsValidatedExtension()
    {
        var ownerId = Guid.NewGuid();

        var fileName = StoredFileNameGenerator.Create(ownerId, ".PDF");

        Assert.True(fileName.StartsWith($"{ownerId:N}_", StringComparison.OrdinalIgnoreCase));
        Assert.True(fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        Assert.True(StoredFileNameGenerator.IsOwnedBy(ownerId, fileName));
        Assert.False(StoredFileNameGenerator.IsOwnedBy(Guid.NewGuid(), fileName));
        Assert.DoesNotContain("/", fileName);
        Assert.DoesNotContain("\\", fileName);
    }

    [Theory]
    [InlineData("../pdf")]
    [InlineData(".")]
    [InlineData(".pdf/")]
    [InlineData(".pdf\\")]
    [InlineData("")]
    public void Create_RejectsInvalidExtension(string extension)
    {
        Assert.Throws<ArgumentException>(() =>
            StoredFileNameGenerator.Create(Guid.NewGuid(), extension));
    }
}
