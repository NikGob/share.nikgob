using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GDriveApi.Models;

public class AuthToken
{
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
