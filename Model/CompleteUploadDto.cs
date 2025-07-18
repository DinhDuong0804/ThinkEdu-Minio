using Microsoft.AspNetCore.Mvc;

namespace ThinkEdu_Minio.Model
{
    public class CompleteUploadDto
    {
        public required  string UploadId { get; set; }

        public required string FileName { get; set; } = null!;

        public required int TotalChunks { get; set; }
    }

}