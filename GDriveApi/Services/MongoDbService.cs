using GDriveApi.Models;
using MongoDB.Driver;

namespace GDriveApi.Services;

public class MongoDbService
{
    private readonly IMongoDatabase _database;

    public MongoDbService(IConfiguration configuration)
    {
        var connectionString = configuration["MongoDb:ConnectionString"]
            ?? throw new InvalidOperationException("MongoDb:ConnectionString is not configured.");
        var databaseName = configuration["MongoDb:DatabaseName"]
            ?? throw new InvalidOperationException("MongoDb:DatabaseName is not configured.");

        var client = new MongoClient(connectionString);
        _database = client.GetDatabase(databaseName);
    }

    public IMongoCollection<UploadEntry> Uploads =>
        _database.GetCollection<UploadEntry>("uploads");

    public IMongoCollection<AuthToken> AuthTokens =>
        _database.GetCollection<AuthToken>("auth-tokens");
}
