using GDriveApi.Models;
using MongoDB.Driver;

namespace GDriveApi.Services;

public class AuthTokenInitializer(MongoDbService mongoDb, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var existingTokens = await mongoDb.AuthTokens
            .Find(Builders<AuthToken>.Filter.Empty)
            .ToListAsync(cancellationToken);

        foreach (var existing in existingTokens)
        {
            if (string.IsNullOrEmpty(existing.Token))
            {
                if (existing.IsActive)
                {
                    await mongoDb.AuthTokens.UpdateOneAsync(
                        Builders<AuthToken>.Filter.Eq(t => t.Id, existing.Id),
                        Builders<AuthToken>.Update.Set(t => t.IsActive, false),
                        cancellationToken: cancellationToken);
                }

                continue;
            }

            if (AuthToken.IsHashed(existing.Token))
                continue;

            await mongoDb.AuthTokens.UpdateOneAsync(
                Builders<AuthToken>.Filter.Eq(t => t.Id, existing.Id)
                & Builders<AuthToken>.Filter.Eq(t => t.Token, existing.Token),
                Builders<AuthToken>.Update.Set(t => t.Token, AuthToken.Hash(existing.Token)),
                cancellationToken: cancellationToken);
        }

        var activeAdminFilter = Builders<AuthToken>.Filter.Eq(t => t.IsActive, true)
            & Builders<AuthToken>.Filter.Eq(t => t.Role, "admin");
        var count = await mongoDb.AuthTokens.CountDocumentsAsync(
            activeAdminFilter, cancellationToken: cancellationToken);

        if (count == 0)
        {
            var configuredToken = configuration["Auth:InitialAdminToken"];
            if (!string.IsNullOrEmpty(configuredToken)
                && (configuredToken.Length < 32 || configuredToken.Any(char.IsWhiteSpace)))
            {
                throw new InvalidOperationException(
                    "Auth:InitialAdminToken must contain at least 32 characters and no whitespace.");
            }

            var rawToken = string.IsNullOrEmpty(configuredToken)
                ? $"{Guid.NewGuid():N}{Guid.NewGuid():N}"
                : configuredToken;
            var tokenHash = AuthToken.Hash(rawToken);
            var token = await mongoDb.AuthTokens
                .Find(Builders<AuthToken>.Filter.Eq(t => t.Token, tokenHash))
                .FirstOrDefaultAsync(cancellationToken);

            if (token is null)
            {
                token = new AuthToken
                {
                    Token = tokenHash,
                    Username = "admin",
                    Role = "admin",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await mongoDb.AuthTokens.InsertOneAsync(token, cancellationToken: cancellationToken);
            }
            else
            {
                await mongoDb.AuthTokens.UpdateOneAsync(
                    Builders<AuthToken>.Filter.Eq(t => t.Id, token.Id),
                    Builders<AuthToken>.Update
                        .Set(t => t.Role, "admin")
                        .Set(t => t.IsActive, true),
                    cancellationToken: cancellationToken);
                token.Role = "admin";
                token.IsActive = true;
            }

            if (string.IsNullOrEmpty(configuredToken))
                Console.WriteLine($"[AUTH] No active admin found. Generated admin token: {rawToken}");
            else
                Console.WriteLine("[AUTH] No active admin found. Activated Auth:InitialAdminToken.");
            Console.WriteLine($"[AUTH] Username: {token.Username} | Role: {token.Role}");
            Console.WriteLine("[AUTH] Use this token in the Authorization header: Bearer <token>");
        }
        else
        {
            Console.WriteLine($"[AUTH] Found {count} active admin(s) in database.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
