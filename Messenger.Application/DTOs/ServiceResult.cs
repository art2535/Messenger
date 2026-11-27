using Messenger.Application.DTOs.Responses;

namespace Messenger.Application.DTOs
{
    /// <summary>
    /// Результат выполнения операции сервиса. Использует общий формат ApiResponse.
    /// </summary>
    public sealed class ServiceResult<T> : ApiResponse<T>
    {
        public static new ServiceResult<T> Success(T? data = default, string? message = null, object? meta = null) => new()
        {
            IsSuccess = true,
            Data = data,
            Message = message,
            Meta = meta
        };

        public static ServiceResult<T> Failure(string error, string? innerError = null,
            string? errorCode = null, string? message = null, T? data = default, object? meta = null) => new()
        {
            IsSuccess = false,
            Error = error,
            InnerError = innerError,
            ErrorCode = errorCode,
            Message = message,
            Data = data,
            Meta = meta
        };
    }
}