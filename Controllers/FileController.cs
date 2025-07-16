using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
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
            if (fileUpload is null || fileUpload is null )
            {
                _logger.LogInformation("File upload request is null or empty.");
                return BadRequest("File must be provided.");
            }
            var result = await _fileService.UploadAsync(fileUpload);

            _logger.LogInformation("File upload completed. Result: {Result}", result);
            return Ok(new { Url = result });
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
            return Ok(new { Message = result });
        }

    }
}