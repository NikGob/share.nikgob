using GDriveApi.Models;
using MongoDB.Driver;

namespace GDriveApi.Services;

public class AuthTokenInitializer(MongoDbService mongoDb) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var activeFilter = Builders<AuthToken>.Filter.Eq(t => t.IsActive, true);
        var count = await mongoDb.AuthTokens.CountDocumentsAsync(activeFilter, cancellationToken: cancellationToken);

        if (count == 0)
        {
            var token = new AuthToken
            {
                Token = $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
                Username = "admin",
                Role = "admin",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await mongoDb.AuthTokens.InsertOneAsync(token, cancellationToken: cancellationToken);
            Console.WriteLine($"[AUTH] No active tokens found. Generated admin token: {token.Token}");
            Console.WriteLine($"[AUTH] Username: {token.Username} | Role: {token.Role}");
            Console.WriteLine("[AUTH] Use this token in the Authorization header: Bearer <token>");
        }
        else
        {
            Console.WriteLine($"[AUTH] Found {count} active token(s) in database.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
