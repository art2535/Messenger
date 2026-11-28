using Messenger.Application.Files;

namespace Messenger.Tests.Shared.TestDoubles;

internal sealed class FakeUploadedFile : IUploadedFile
{
    private readonly byte[] _content;

    public FakeUploadedFile(string fileName, string contentType, byte[] content)
    {
        FileName = fileName;
        ContentType = contentType;
        _content = content;
    }

    public string FileName { get; }
    public string ContentType { get; }
    public long Length => _content.Length;
    public Stream OpenReadStream() => new MemoryStream(_content, writable: false);
}
