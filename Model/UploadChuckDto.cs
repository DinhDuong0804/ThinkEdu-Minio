namespace ThinkEdu_Minio.Model
{
    public class UploadChunkDto
    {
        public required IFormFile Chunk { get; set; } 

        public required string UploadId { get; set; }

        public required int ChunkIndex { get; set; }
    }

}
