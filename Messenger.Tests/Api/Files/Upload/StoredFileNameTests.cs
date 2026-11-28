using System.Reflection;
using Messenger.API.Controllers;
using Xunit;

namespace Messenger.Tests.Api.Files.Upload;

public sealed class StoredFileNameTests
{
    [Fact]
    public void CreateStoredFileName_EmbedsOwnerIdAndKeepsValidatedExtension()
    {
        var ownerId = Guid.NewGuid();
        var method = typeof(MessagesController).GetMethod(
            "CreateStoredFileName",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var fileName = (string)method!.Invoke(null, new object[] { ownerId, "report.pdf" })!;

        Assert.StartsWith($"{ownerId:N}_", fileName, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".pdf", fileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/", fileName);
        Assert.DoesNotContain("\\", fileName);
    }
}
