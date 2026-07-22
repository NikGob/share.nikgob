using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GDriveApi.Models;

public class UploadEntry
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("slug")]
    public string Slug { get; set; } = string.Empty;

    [BsonElement("fileName")]
    public string FileName { get; set; } = string.Empty;

    [BsonElement("contentType")]
    public string ContentType { get; set; } = string.Empty;

    [BsonElement("fileSize")]
    [BsonRepresentation(BsonType.Int64)]
    public long FileSize { get; set; }

    [BsonElement("googleDriveFileId")]
    public string GoogleDriveFileId { get; set; } = string.Empty;

    [BsonElement("shortUrl")]
    public string ShortUrl { get; set; } = string.Empty;

    [BsonElement("longUrl")]
    public string LongUrl { get; set; } = string.Empty;

    [BsonElement("collection")]
    public string Collection { get; set; } = "default";

    [BsonElement("title")]
    public string? Title { get; set; }

    [BsonElement("crawlable")]
    public bool? Crawlable { get; set; }

    [BsonElement("ownerId")]
    public ObjectId OwnerId { get; set; }

    [BsonElement("skipImageServing")]
    public bool SkipImageServing { get; set; } = false;

    [BsonElement("isDeleting")]
    [BsonIgnoreIfDefault]
    public bool IsDeleting { get; set; }

    [BsonElement("deleteClaimedAt")]
    [BsonIgnoreIfNull]
    public DateTime? DeleteClaimedAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
