using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using ThinkEdu_Minio.Configurations;
using ThinkEdu_Minio.Services.Interfaces;

namespace ThinkEdu_Minio.Services.Implements
{
    public class FileServices : IFileService
    {
        private readonly IMinioClient _minioClient;
        private readonly MinioSettings _settings;
        private readonly ILogger<FileServices> _logger;

        public FileServices(IOptions<MinioSettings> options, ILogger<FileServices> logger)
        {
            _settings = options.Value;
            _logger = logger;

            _minioClient = new MinioClient()
                .WithEndpoint(_settings.Endpoint)
                .WithCredentials(_settings.AccessKey, _settings.SecretKey)
                .WithSSL(_settings.WithSSL)
                .Build();
        }

        // Tạo URL công khai cho file đã upload
        private string GeneratePublicUrl(string objectKey)
        {
            var baseUrl = _settings.PublicUrlBase.Trim().TrimEnd('/');
            return $"{baseUrl}/{_settings.BucketName}/{objectKey}";
        }

        // xử lý URL để lấy object key (file name)
        private string ExtractObjectKey(string url)
        {
            var baseUrl = _settings.PublicUrlBase.Trim().TrimEnd('/');
            if (!url.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("URL không bắt đầu bằng PublicUrlBase.");

            var path = url.Substring(baseUrl.Length).TrimStart('/');

            if (!path.StartsWith(_settings.BucketName + "/"))
                throw new ArgumentException("URL không chứa bucket hợp lệ.");

            // xử lý trường hợp có query string - nếu có thì bỏ qua phần query 
            var objectKey = path.Substring(_settings.BucketName.Length + 1);
            var queryIndex = objectKey.IndexOf('?');

            return queryIndex >= 0 ? objectKey[..queryIndex] : objectKey;
        }

        // Kiểm tra xem file đã tồn tại trong bucket chưa
        private async Task<bool> FileExistsAsync(string bucket, string objectKey)
        {
            try
            {
                await _minioClient.StatObjectAsync(new StatObjectArgs()
                    .WithBucket(bucket)
                    .WithObject(objectKey));

                return true;
            }
            catch (ObjectNotFoundException)
            {
                return false;
            }
        }

        public async Task<string> UploadAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return "File không hợp lệ.";

            try
            {
                var bucket = _settings.BucketName;

                // Kiểm tra và tạo bucket nếu chưa tồn tại
                var bucketExists = await _minioClient.BucketExistsAsync(
                    new BucketExistsArgs().WithBucket(bucket));

                if (!bucketExists)
                {
                    await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket));
                }

                string fileName = Path.GetFileName(file.FileName);
                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);

                string objectKey = fileName;
                int counter = 0;

                //tự động tăng hậu tố nếu file  đã tồn tại
                while (await FileExistsAsync(bucket, objectKey))
                {
                    counter++;
                    objectKey = $"{nameWithoutExt}_{counter}{extension}";
                }

                using var stream = file.OpenReadStream();

                await _minioClient.PutObjectAsync(new PutObjectArgs()
                    .WithBucket(bucket)
                    .WithObject(objectKey)
                    .WithStreamData(stream)
                    .WithObjectSize(file.Length)
                    .WithContentType(file.ContentType));

                return GeneratePublicUrl(objectKey);

            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Lỗi khi upload file lên MinIO.");
                return "Upload file thất bại.";
            }
        }

        public async Task<string> DeleteAsync(string publicUrl)
        {
            if (string.IsNullOrWhiteSpace(publicUrl))
                return "URL không hợp lệ.";

            try
            {
                var objectKey = ExtractObjectKey(publicUrl);

                await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                    .WithBucket(_settings.BucketName)
                    .WithObject(objectKey));

                _logger.LogInformation("Đã xóa file MinIO: {ObjectKey}", objectKey);
                return "Xóa file thành công.";
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Lỗi khi xóa file khỏi MinIO. URL: {Url}", publicUrl);
                return "Xóa file thất bại.";
            }
        }

    }
}
