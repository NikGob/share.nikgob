namespace GDriveApi.Dtos;

public class CollectionDto
{
    public string Name { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
