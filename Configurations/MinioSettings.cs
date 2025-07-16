namespace ThinkEdu_Minio.Configurations
{
    public class MinioSettings
    {
        public string Endpoint { get; set; } = null!;
        public string AccessKey { get; set; } = null!;
        public string SecretKey { get; set; } = null!;
        public string BucketName { get; set; } = null!;
        public bool WithSSL { get; set; }
        public string PublicUrlBase { get; set; } = null!;
    }
}