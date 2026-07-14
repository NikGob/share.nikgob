using GDriveApi.Dtos;

namespace GDriveApi.Interfaces;

public interface IShlinkService
{
    Task<ShlinkResult> CreateShortUrlAsync(string longUrl, string customSlug, string? title, bool? crawlable);
    Task<ShlinkResult> UpdateShortUrlAsync(string shortCode, string? longUrl, string? title, bool? crawlable);
    Task DeleteShortUrlAsync(string shortCode);
    Task<ShlinkResult> GetShortUrlAsync(string shortCode);
}
