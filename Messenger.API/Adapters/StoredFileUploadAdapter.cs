using Messenger.Application.Files;

namespace Messenger.API.Adapters;

/// <summary>Адаптер уже сохранённого файла к контракту проверки содержимого.</summary>
internal sealed class StoredFileUploadAdapter : IUploadedFile
{
    private readonly Stream _stream;

    public StoredFileUploadAdapter(string fileName, long length, Stream stream)
    {
        FileName = fileName;
        Length = length;
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public string FileName { get; }
    public string ContentType => "application/octet-stream";
    public long Length { get; }

    public Stream OpenReadStream()
    {
        if (_stream.CanSeek)
            _stream.Position = 0;

        return _stream;
    }
}
