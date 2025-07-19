using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using ThinkEdu_Minio.Model;
using ThinkEdu_Minio.Services.Interfaces;

namespace ThinkEdu_Minio.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Consumes("multipart/form-data")]

    public class FileController : ControllerBase
    {
        private readonly IFileService _fileService;
        private readonly ILogger<FileController> _logger;

        public FileController(IFileService fileService, ILogger<FileController> logger)
        {
            _fileService = fileService;
            _logger = logger;
        }

        [HttpPost]
        [SwaggerOperation("Upload file")]
        public async Task<IActionResult> UploadFile(IFormFile fileUpload)
        {
            _logger.LogInformation("Received file upload request.");

            // Check file type - only accept PNG, SVG, JPG
            if (fileUpload != null)
            {
                var extension = Path.GetExtension(fileUpload.FileName).ToLowerInvariant();
                if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".svg")
                {
                    _logger.LogWarning("Invalid file type: {FileType}. Only PNG, SVG, and JPG files are allowed.", extension);
                    return BadRequest("Only PNG, SVG, and JPG files are allowed.");
                }
            }

            if (fileUpload is null)
            {
                _logger.LogInformation("File upload request is null or empty.");
                return BadRequest("File must be provided.");
            }

            var response = await _fileService.UploadAsync(fileUpload);

            _logger.LogInformation("File upload completed. Result: {Result}", response);

            return Ok(response);
        }

        [HttpDelete]
        [SwaggerOperation("Delete file")]
        public async Task<IActionResult> DeleteFile(string url)
        {
            _logger.LogInformation("Received file delete request for URL: {Url}", url);
            if (string.IsNullOrEmpty(url))
            {
                _logger.LogInformation("request URL is null or empty.");
                return BadRequest("URL must be provided.");
            }
            var result = await _fileService.DeleteAsync(url);
            _logger.LogInformation("File delete completed. Result: {Result}", result);
            return Ok(result);
        }



        [HttpPost("upload-chunk")]
        [SwaggerOperation("Upload file chunk")]
        public async Task<IActionResult> UploadChunk([FromForm] UploadChunkDto request)
        {
            _logger.LogInformation("Received file chunk upload request for uploadId: {UploadId}, chunkIndex: {ChunkIndex}", request.UploadId, request.ChunkIndex);

            if (request.Chunk == null || string.IsNullOrWhiteSpace(request.UploadId) || request.ChunkIndex < 0)
            {
                _logger.LogInformation("Chunk upload request is invalid.");
                return BadRequest("Invalid chunk upload request.");
            }

            var response = await _fileService.UploadChunkAsync(request.Chunk, request.UploadId, request.ChunkIndex);
            return Ok(response);
        }

        [HttpPost("complete-upload")]
        [SwaggerOperation("Complete file upload")]
        public async Task<IActionResult> CompleteUpload([FromForm] CompleteUploadDto request)
        {
            _logger.LogInformation("Received complete upload request for uploadId: {UploadId}, fileName: {FileName}, totalChunks: {TotalChunks}", request.UploadId, request.FileName, request.TotalChunks);

            if (string.IsNullOrWhiteSpace(request.UploadId) || string.IsNullOrWhiteSpace(request.FileName) || request.TotalChunks <= 0)
            {
                _logger.LogInformation("Complete upload request is invalid.");
                return BadRequest("Invalid complete upload request.");
            }

            var response = await _fileService.CompleteUploadAsync(request.UploadId, request.FileName, request.TotalChunks);
            _logger.LogInformation("File upload completion request processed. Result: {Result}", response);
            return Ok(response);
        }

        [HttpDelete("{uploadId}/cancel-upload")]
        [SwaggerOperation("Cancel file upload")]
        public async Task<IActionResult> CancelUpload(string uploadId)
        {
            _logger.LogInformation("Received cancel upload request for uploadId: {UploadId}", uploadId);
            if (string.IsNullOrWhiteSpace(uploadId))
            {
                _logger.LogInformation("Cancel upload request is invalid. UploadId is null or empty.");
                return BadRequest("UploadId must be provided.");
            }
            var response = await _fileService.HuyUploadAsync(uploadId);
            _logger.LogInformation("File upload cancellation request processed. Result: {Result}", response);
            return Ok(response);
        }

    }
}