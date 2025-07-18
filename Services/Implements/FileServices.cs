using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using System.Net;
using ThinkEdu_Minio.Configurations;
using ThinkEdu_Minio.Model;
using ThinkEdu_Minio.Services.Interfaces;

namespace ThinkEdu_Minio.Services.Implements
{
    public class FileServices : IFileService
    {
        private readonly IMinioClient _minioClient;
        private readonly MinioSettings _settings;
        private readonly ILogger<FileServices> _logger;
        private const string TEMP_CHUNKS_DIR = "TempChunks";

        public FileServices(IOptions<MinioSettings> options, ILogger<FileServices> logger)
        {
            _settings = options.Value;
            _logger = logger;

            _minioClient = new MinioClient()
                .WithEndpoint(_settings.Endpoint)
                .WithCredentials(_settings.AccessKey, _settings.SecretKey)
                .WithSSL(_settings.WithSSL)
                .Build();

            // Tự động dọn dẹp file tạm cũ khi khởi động
            Task.Run(() => DonDepThuMucTamCuAsync(TimeSpan.FromDays(1)));
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

        public async Task<BaseResponse<string>> UploadAsync(IFormFile file)
        {
            var response = new BaseResponse<string>()
            {
                Success = false,
            };
            if (file == null || file.Length == 0)
            {
                response.Message = "File không hợp lệ hoặc rỗng.";
                _logger.LogInformation("File upload request is null or empty.");
                return response;
            }

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

                //tự động tăng hậu tố nếu file đã tồn tại
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

                var url = GeneratePublicUrl(objectKey);

                response.Success = true;
                response.ResultCode = HttpStatusCode.OK;
                response.Message = "Upload file thành công.";
                response.Data = url;
                _logger.LogInformation("Đã upload file lên MinIO: {ObjectKey}, URL: {Url}, ContentType: {ContentType}", 
                    objectKey, url, file.ContentType);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Lỗi khi upload file lên MinIO.");

                response.Message = $"Lỗi khi upload file: {ex}";
                response.ResultCode = HttpStatusCode.InternalServerError;
                return response;
            }
        }

        // xóa file khỏi MinIO bằng URL công khai
        public async Task<BaseResponse<string>> DeleteAsync(string publicUrl)
        {
            var response = new BaseResponse<string>()
            {
                Success = false,
            };
            if (string.IsNullOrWhiteSpace(publicUrl))
            {
                response.Message = "URL không hợp lệ.";
                _logger.LogInformation("Delete request URL is null or empty.");
                return response;
            }

            try
            {
                var objectKey = ExtractObjectKey(publicUrl);

                await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                    .WithBucket(_settings.BucketName)
                    .WithObject(objectKey));

                _logger.LogInformation("Đã xóa file MinIO: {ObjectKey}", objectKey);

                response.Success = true;
                response.ResultCode = HttpStatusCode.OK;
                response.Message = "Xóa file thành công.";

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Lỗi khi xóa file khỏi MinIO. URL: {Url}", publicUrl);

                response.Message = $"Lỗi khi xóa file: {ex.Message}";
                response.ResultCode = HttpStatusCode.InternalServerError;
                return response;
            }
        }


        // Lưu từng chunk vào thư mục tạm (TempChunks/<uploadId>/<chunkIndex>.part)
        public async Task<BaseResponse<string>> UploadChunkAsync(IFormFile chunk, string uploadId, int chunkIndex)
        {
            var response = new BaseResponse<string>()
            {
                Success = false,
            };

            if (chunk == null || chunk.Length == 0)
            {
                response.Message = "Chunk không hợp lệ hoặc rỗng.";
                _logger.LogInformation("Chunk upload request is null or empty.");
                return response;
            }

            try
            {
                var tempPath = Path.Combine(TEMP_CHUNKS_DIR, uploadId);
                Directory.CreateDirectory(tempPath);

                var chunkPath = Path.Combine(tempPath, $"{chunkIndex}.part");
                using var stream = new FileStream(chunkPath, FileMode.Create);
                await chunk.CopyToAsync(stream);

               
                response.Success = true;
                response.ResultCode = HttpStatusCode.OK;
                response.Message = $"Chunk {chunkIndex} uploaded.";
                response.Data = chunkPath;

                _logger.LogInformation("Uploaded chunk {ChunkIndex} for upload session {UploadId}", chunkIndex, uploadId);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi upload chunk {ChunkIndex} cho session {UploadId}", chunkIndex, uploadId);
                response.Message = $"Lỗi khi upload chunk: {ex.Message}";
                response.ResultCode = HttpStatusCode.InternalServerError;
                return response;
            }
        }

        // Ghép các chunk lại, upload lên MinIO, rồi cleanup
        public async Task<BaseResponse<string>> CompleteUploadAsync(string uploadId, string fileName, int totalChunks)
        {
            var response = new BaseResponse<string>()
            {
                Success = false,
            };

            try
            {
                var tempPath = Path.Combine(TEMP_CHUNKS_DIR, uploadId);
                var finalPath = Path.Combine(tempPath, "final.tmp");

                // Ghép tất cả chunk
                await using (var output = new FileStream(finalPath, FileMode.Create))
                {
                    for (int i = 0; i < totalChunks; i++)
                    {
                        var chunkFile = Path.Combine(tempPath, $"{i}.part");
                        if (!File.Exists(chunkFile))
                        {
                            response.Message = $"Thiếu chunk {i}.";
                            response.ResultCode = HttpStatusCode.BadRequest;
                            _logger.LogWarning("Missing chunk {ChunkIndex} for session {UploadId}", i, uploadId);
                            return response;
                        }
                        await using var input = new FileStream(chunkFile, FileMode.Open);
                        await input.CopyToAsync(output);
                    }
                }

                var bucket = _settings.BucketName;

                // Kiểm tra và tạo bucket nếu chưa tồn tại
                var bucketExists = await _minioClient.BucketExistsAsync(
                    new BucketExistsArgs().WithBucket(bucket));
                if (!bucketExists)
                    await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket));

                string objectKey = fileName;
                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);
                int counter = 0;

                // Tự động tăng hậu tố nếu file đã tồn tại
                while (await FileExistsAsync(bucket, objectKey))
                {
                    counter++;
                    objectKey = $"{nameWithoutExt}_{counter}{extension}";
                }

                // Xác định content type từ extension trước để đảm bảo có giá trị tốt nhất có thể
                string contentType = GetMimeTypeFromFileName(fileName);
                _logger.LogInformation("Content type từ extension: {ContentType}", contentType);
                
                //// Sau đó kiểm tra nếu có content-type từ file (chỉ sử dụng nếu không phải là octet-stream)
                //var contentTypePath = Path.Combine(tempPath, "content-type.txt");
                //if (File.Exists(contentTypePath))
                //{
                //    var storedContentType = File.ReadAllText(contentTypePath).Trim();
                //    if (!string.IsNullOrEmpty(storedContentType) && 
                //        storedContentType != "application/octet-stream")
                //    {
                //        contentType = storedContentType;
                //        _logger.LogInformation("Đã ghi đè content type từ file: {ContentType}", contentType);
                //    }
                //    else
                //    {
                //        _logger.LogInformation("Bỏ qua content type từ file vì là loại generic: {StoredType}", storedContentType);
                //    }
                //}

                // Upload lên MinIO, đảm bảo stream được dispose trước khi xóa folder
                await using (var finalStream = new FileStream(finalPath, FileMode.Open))
                {
                    await _minioClient.PutObjectAsync(new PutObjectArgs()
                        .WithBucket(bucket)
                        .WithObject(objectKey)
                        .WithStreamData(finalStream)
                        .WithObjectSize(finalStream.Length)
                        .WithContentType(contentType));
                } // <-- finalStream DISPOSED here!

                var url = GeneratePublicUrl(objectKey);

                // Dọn dẹp
                await XoaThuMucTamUploadAsync(uploadId);

                response.Success = true;
                response.ResultCode = HttpStatusCode.OK;
                response.Message = "Upload completed";
                response.Data = url;

                _logger.LogInformation("Ghép và upload file từ session {UploadId} thành công. URL: {Url}, ContentType: {ContentType}", 
                    uploadId, url, contentType);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi hoàn tất upload cho session {UploadId}", uploadId);
                response.Message = $"Lỗi khi hoàn tất upload: {ex.Message}";
                response.ResultCode = HttpStatusCode.InternalServerError;
                return response;
            }
        }

        /// <summary>
        /// Hủy bỏ quá trình upload và xóa các file tạm
        /// </summary>
        public async Task<BaseResponse<bool>> HuyUploadAsync(string uploadId)
        {
            var response = new BaseResponse<bool>()
            {
                Success = false,
                Data = false
            };

            try
            {
                await XoaThuMucTamUploadAsync(uploadId);

                response.Success = true;
                response.ResultCode = HttpStatusCode.OK;
                response.Message = "Đã hủy upload và xóa file tạm";
                response.Data = true;

                _logger.LogInformation("Đã hủy và xóa file tạm cho session {UploadId}", uploadId);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi hủy upload {UploadId}", uploadId);
                response.Message = $"Lỗi khi hủy upload: {ex.Message}";
                response.ResultCode = HttpStatusCode.InternalServerError;
                return response;
            }
        }

        /// <summary>
        /// Xóa thư mục tạm cho một phiên upload
        /// </summary>
        private Task XoaThuMucTamUploadAsync(string uploadId)
        {
            return Task.Run(() =>
            {
                try
                {
                    var tempPath = Path.Combine(TEMP_CHUNKS_DIR, uploadId);
                    if (Directory.Exists(tempPath))
                    {
                        // Xóa thư mục và tất cả các file bên trong
                        Directory.Delete(tempPath, true);
                        _logger.LogInformation("Đã xóa thư mục tạm: {TempPath}", tempPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi xóa thư mục tạm cho {UploadId}", uploadId);
                    throw; // Ném lại ngoại lệ để người gọi biết việc xóa thất bại
                }
            });
        }

        /// <summary>
        /// Xóa các thư mục tạm cũ hơn thời gian chỉ định
        /// </summary>
        public Task DonDepThuMucTamCuAsync(TimeSpan tuoiToiDa)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(TEMP_CHUNKS_DIR))
                    {
                        Directory.CreateDirectory(TEMP_CHUNKS_DIR);
                        return;
                    }

                    var thoiDiemCutoff = DateTime.UtcNow.Subtract(tuoiToiDa);
                    var dsThuMucTam = Directory.GetDirectories(TEMP_CHUNKS_DIR);

                    foreach (var thuMuc in dsThuMucTam)
                    {
                        try
                        {
                            var thongTinThuMuc = new DirectoryInfo(thuMuc);
                            if (thongTinThuMuc.CreationTimeUtc < thoiDiemCutoff)
                            {
                                Directory.Delete(thuMuc, true);
                                _logger.LogInformation("Đã xóa thư mục tạm cũ: {TempDir}", thuMuc);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Lỗi khi xóa thư mục tạm cũ: {TempDir}", thuMuc);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi dọn dẹp thư mục tạm");
                }
            });
        }

        // Helper method để xác định MIME type từ tên file
        private string GetMimeTypeFromFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                ".svg" => "image/svg+xml",
                ".mp4" => "video/mp4",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".txt" => "text/plain",
                ".html" or ".htm" => "text/html",
                ".css" => "text/css",
                ".js" => "text/javascript",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".zip" => "application/zip",
                ".doc" or ".docx" => "application/msword",
                ".xls" or ".xlsx" => "application/vnd.ms-excel",
                ".ppt" or ".pptx" => "application/vnd.ms-powerpoint",
                _ => "application/octet-stream"
            };
        }
    }

    public partial class FileService
}