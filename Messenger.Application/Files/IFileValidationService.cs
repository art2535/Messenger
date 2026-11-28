namespace Messenger.Application.Files;

public interface IFileValidationService
{
    Task<ValidatedFile> ValidateAsync(
        IUploadedFile file,
        FileUploadPurpose purpose,
        CancellationToken cancellationToken = default);
}
