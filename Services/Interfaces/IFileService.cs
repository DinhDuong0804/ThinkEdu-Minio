using ThinkEdu_Minio.Model;

namespace ThinkEdu_Minio.Services.Interfaces
{
    public interface IFileService
    {
        Task<BaseResponse<string>> UploadAsync(IFormFile file);

        Task<BaseResponse<string>> DeleteAsync(string publicUrl);

        Task<BaseResponse<string>> UploadChunkAsync(IFormFile chunk, string uploadId, int chunkIndex);

        Task<BaseResponse<string>> CompleteUploadAsync(string uploadId, string fileName, int totalChunks);

        Task<BaseResponse<bool>> HuyUploadAsync(string uploadId);
    }
}