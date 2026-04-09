using System.ComponentModel.DataAnnotations;

namespace GDriveApi.Requests;

public class UploadFileRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;

    public string? Slug { get; set; }

    public int SlugLength { get; set; } = 8;

    public string Collection { get; set; } = "default";

    public string? Title { get; set; }

    public bool? Crawlable { get; set; }

    public string? CustomFileName { get; set; }

    public bool RenameFile { get; set; } = false;

    public bool NoCompression { get; set; } = false;
}
