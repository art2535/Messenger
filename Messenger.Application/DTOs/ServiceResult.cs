namespace Messenger.Core.DTOs
{
    public class ServiceResult<T>
    {
        public bool isSuccess { get; set; }
        public T? data { get; set; }
        public string? error { get; set; }
        public string? innerError { get; set; }

        public static ServiceResult<T> Success(T data) => new()
        {
            isSuccess = true,
            data = data
        };

        public static ServiceResult<T> Failure(string error, string? innerError = null) => new()
        {
            isSuccess = false,
            error = error,
            innerError = innerError
        };
    }
}
