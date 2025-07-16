namespace ThinkEdu_Minio.Services.Interfaces
{
    public interface IFileService
    {
        Task<string> UploadAsync(IFormFile file);
        Task<string> DeleteAsync(string publicUrl);
    }
}