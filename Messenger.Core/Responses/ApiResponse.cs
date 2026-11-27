using System.Text.Json.Serialization;

namespace Messenger.Core.Responses
{
    public class ApiResponse<T>
    {
        public required bool IsSuccess { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Message { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Error { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ErrorCode { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? InnerError { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public T? Data { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Meta { get; set; }

        public static ApiResponse<T> Success(T? data = default, string? message = null, object? meta = null) => new()
        {
            IsSuccess = true,
            Data = data,
            Message = message,
            Meta = meta
        };

        public static ApiResponse<T> Failure(string error, string? errorCode = null, string? message = null,
            T? data = default, object? meta = null, string? innerError = null) => new()
        {
            IsSuccess = false,
            Error = error,
            ErrorCode = errorCode,
            InnerError = innerError,
            Message = message,
            Data = data,
            Meta = meta
        };
    }

    public static class ApiResponse
    {
        public static ApiResponse<T> Success<T>(T? data = default, string? message = null, object? meta = null) =>
            ApiResponse<T>.Success(data, message, meta);

        public static ApiResponse<object> Success(string? message = null, object? meta = null) =>
            ApiResponse<object>.Success(null, message, meta);

        public static ApiResponse<object> Failure(string error, string? errorCode = null, string? message = null,
            object? meta = null) =>
            ApiResponse<object>.Failure(error, errorCode, message, null, meta);

        public static ApiResponse<T> Failure<T>(string error, string? errorCode = null, string? message = null,
            T? data = default, object? meta = null) =>
            ApiResponse<T>.Failure(error, errorCode, message, data, meta);
    }
}
