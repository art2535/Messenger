namespace Messenger.Application.Files;

/// <summary>
/// Нейтральное представление входящего файла для прикладных сценариев.
/// Реализация отвечает за открытие потока; вызывающий код владеет возвращённым потоком.
/// </summary>
public interface IUploadedFile
{
    string FileName { get; }
    string ContentType { get; }
    long Length { get; }
    Stream OpenReadStream();
}
