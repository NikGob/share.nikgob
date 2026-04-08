namespace GDriveApi.Requests;

public class UpdateFileRequest
{
    public string? NewSlug { get; set; }
    public string? Title { get; set; }
    public bool? Crawlable { get; set; }
}
