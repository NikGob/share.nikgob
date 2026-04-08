using GDriveApi.Interfaces;
using GDriveApi.Middlewares;
using Microsoft.AspNetCore.Mvc;

namespace GDriveApi.Controllers;

[Route("api/v1/collections")]
[ApiController]
[AuthMiddleware]
[Produces("application/json")]
public class CollectionsController(IFileManagerService fileManager) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCollections()
    {
        try
        {
            var collections = await fileManager.GetCollectionsAsync();
            return Ok(collections);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetCollections] Error: {ex.Message}");
            return StatusCode(500, new { message = "Internal server error." });
        }
    }

    [HttpGet("{name}")]
    public async Task<IActionResult> GetByCollection(string name)
    {
        try
        {
            var files = await fileManager.GetFilesByCollectionAsync(name);
            return Ok(files);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetByCollection] Error: {ex.Message}");
            return StatusCode(500, new { message = "Internal server error." });
        }
    }
}
