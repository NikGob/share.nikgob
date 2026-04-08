using GDriveApi.Middlewares;
using GDriveApi.Models;
using GDriveApi.Requests;
using GDriveApi.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GDriveApi.Controllers;

[Route("api/v1/accounts")]
[ApiController]
[AuthMiddleware]
[Produces("application/json")]
public class AccountsController(MongoDbService mongoDb) : ControllerBase
{
    private AuthToken CurrentUser => (AuthToken)HttpContext.Items["AuthUser"]!;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest request)
    {
        if (CurrentUser.Role != "admin")
            return StatusCode(403, new { message = "Only admins can create accounts." });

        var token = new AuthToken
        {
            Token = $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
            Username = request.Username,
            Role = request.Role,
            IsActive = true,
            CreatedBy = CurrentUser.Id,
            CreatedAt = DateTime.UtcNow
        };

        await mongoDb.AuthTokens.InsertOneAsync(token);

        return Ok(new
        {
            id = token.Id.ToString(),
            username = token.Username,
            role = token.Role,
            token = token.Token,
            createdAt = token.CreatedAt
        });
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (CurrentUser.Role != "admin")
            return StatusCode(403, new { message = "Only admins can list accounts." });

        var accounts = await mongoDb.AuthTokens
            .Find(Builders<AuthToken>.Filter.Empty)
            .ToListAsync();

        var result = accounts.Select(a => new
        {
            id = a.Id.ToString(),
            username = a.Username,
            role = a.Role,
            isActive = a.IsActive,
            createdAt = a.CreatedAt
        });

        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Deactivate(string id)
    {
        if (CurrentUser.Role != "admin")
            return StatusCode(403, new { message = "Only admins can deactivate accounts." });

        if (!ObjectId.TryParse(id, out var objectId))
            return BadRequest(new { message = "Invalid account ID." });

        if (objectId == CurrentUser.Id)
            return BadRequest(new { message = "You cannot deactivate your own account." });

        var result = await mongoDb.AuthTokens.UpdateOneAsync(
            Builders<AuthToken>.Filter.Eq(t => t.Id, objectId),
            Builders<AuthToken>.Update.Set(t => t.IsActive, false));

        if (result.MatchedCount == 0)
            return NotFound(new { message = "Account not found." });

        return Ok(new { message = "Account deactivated." });
    }
}
