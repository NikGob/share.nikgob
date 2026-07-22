using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GDriveApi.Models;

public class AuthToken
{
    private const string HashPrefix = "sha256:";

    public static string Hash(string token) =>
        HashPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static bool IsHashed(string? token)
    {
        if (token is null
            || token.Length != HashPrefix.Length + 64
            || !token.StartsWith(HashPrefix, StringComparison.Ordinal))
            return false;

        foreach (var character in token.AsSpan(HashPrefix.Length))
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        }

        return true;
    }

    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("token")]
    public string Token { get; set; } = string.Empty;

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("role")]
    public string Role { get; set; } = "user";

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("createdBy")]
    public ObjectId? CreatedBy { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
