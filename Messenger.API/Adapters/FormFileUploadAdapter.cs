using Messenger.Application.Files;
using Microsoft.AspNetCore.Http;

namespace Messenger.API.Adapters;

/// <summary>Адаптер HTTP-загрузки ASP.NET к нейтральному контракту Application.</summary>
internal sealed class FormFileUploadAdapter : IUploadedFile
{
    private readonly IFormFile _file;

    public FormFileUploadAdapter(IFormFile file)
    {
        _file = file ?? throw new ArgumentNullException(nameof(file));
    }

    public string FileName => _file.FileName;
    public string ContentType => _file.ContentType;
    public long Length => _file.Length;
    public Stream OpenReadStream() => _file.OpenReadStream();
}
