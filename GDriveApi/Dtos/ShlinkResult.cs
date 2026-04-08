namespace GDriveApi.Dtos;

public class ShlinkResult
{
    public string ShortCode { get; set; } = string.Empty;
    public string ShortUrl { get; set; } = string.Empty;
    public string LongUrl { get; set; } = string.Empty;
    public string? Title { get; set; }
    public bool? Crawlable { get; set; }
}
