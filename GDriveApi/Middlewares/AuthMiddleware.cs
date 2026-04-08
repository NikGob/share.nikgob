using GDriveApi.Models;
using GDriveApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MongoDB.Driver;

namespace GDriveApi.Middlewares;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AuthMiddleware : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var mongoDb = context.HttpContext.RequestServices.GetRequiredService<MongoDbService>();

        var authHeader = context.HttpContext.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Missing or invalid Authorization header. Use: Bearer <token>" });
            return;
        }

        var token = authHeader["Bearer ".Length..].Trim();

        var filter = Builders<AuthToken>.Filter.Eq(t => t.Token, token)
            & Builders<AuthToken>.Filter.Eq(t => t.IsActive, true);

        var found = await mongoDb.AuthTokens.Find(filter).FirstOrDefaultAsync();

        if (found == null)
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Invalid or inactive token." });
            return;
        }

        context.HttpContext.Items["AuthUser"] = found;

        await next();
    }
}
