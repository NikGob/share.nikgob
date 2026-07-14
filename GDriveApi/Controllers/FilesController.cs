using System.ComponentModel.DataAnnotations;
using GDriveApi.Interfaces;
using GDriveApi.Middlewares;
using GDriveApi.Models;
using GDriveApi.Requests;
using Microsoft.AspNetCore.Mvc;

namespace GDriveApi.Controllers;

[Route("api/v1/files")]
[ApiController]
[AuthMiddleware]
[Produces("application/json")]
public class FilesController(IFileManagerService fileManager) : ControllerBase
{
    private AuthToken CurrentUser => (AuthToken)HttpContext.Items["AuthUser"]!;

    private static string ExtractSlug(string slugOrUrl)
    {
        slugOrUrl = Uri.UnescapeDataString(slugOrUrl);

        if (slugOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            slugOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var path = new Uri(slugOrUrl).AbsolutePath.TrimEnd('/');
            var lastSegment = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
            if (!string.IsNullOrEmpty(lastSegment))
                return lastSegment;
        }

        return slugOrUrl;
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload([FromForm] UploadFileRequest request)
    {
        try
        {
            var result = await fileManager.UploadFileAsync(request, CurrentUser);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Upload] Error: {ex.Message}");
            return StatusCode(500, new { message = "Internal server error during upload." });
        }
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetInfo(string slug)
    {
        slug = ExtractSlug(slug);
        try
        {
            var info = await fileManager.GetFileInfoAsync(slug);
            return Ok(info);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetInfo] Error: {ex.Message}");
            return StatusCode(500, new { message = "Internal server error." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetSlugs(
        [FromQuery] string? collection = null,
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, 500)] int pageSize = 100)
    {
        var slugs = await fileManager.GetSlugsAsync(collection, page, pageSize);
        return Ok(slugs);
    }

    [HttpDelete("{slug}")]
    public async Task<IActionResult> Delete(string slug)
    {
        slug = ExtractSlug(slug);
        try
        {
            await fileManager.DeleteFileAsync(slug, CurrentUser);
            return Ok(new { message = $"File with slug '{slug}' deleted successfully." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Delete] Error: {ex.Message}");
            return StatusCode(500, new { message = "Internal server error." });
        }
    }

    [HttpPatch("{slug}")]
    public async Task<IActionResult> Update(string slug, [FromBody] UpdateFileRequest request)
    {
        slug = ExtractSlug(slug);
        try
        {
            var result = await fileManager.UpdateFileAsync(slug, request, CurrentUser);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Update] Error: {ex.Message}");
            return StatusCode(500, new { message = "Internal server error." });
        }
    }
}
