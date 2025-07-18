using System.Net;

namespace ThinkEdu_Minio.Model
{
    public class BaseResponse<T>
    {
        public bool Success { get; set; }
        public HttpStatusCode ResultCode { get; set; }
        public object? Message { get; set; }
        public List<string>? ValidationErrors { get; set; }
        public T? Data { get; set; }

        public BaseResponse()
        {
            Success = true;
            ResultCode = HttpStatusCode.OK;
        }

        public BaseResponse(bool success)
        {
            Success = success;
        }

        public BaseResponse(string message)
        {
            Success = true;
            Message = message;
            ResultCode = HttpStatusCode.OK;
        }

        public BaseResponse(string message, bool success)
        {
            Success = success;
            Message = message;
        }

        public BaseResponse(string message, bool success, HttpStatusCode resultCode)
        {
            Success = success;
            Message = message;
            ResultCode = resultCode;
        }

        public BaseResponse(T data, string? message = null)
        {
            Success = true;
            Message = message;
            ResultCode = HttpStatusCode.OK;
            Data = data;
        }

        public BaseResponse(T data, HttpStatusCode resultCode, string? message = null)
        {
            Success = true;
            Message = message;
            ResultCode = resultCode;
            Data = data;
            ValidationErrors = null;
        }

    }

}
