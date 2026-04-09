using GDriveApi.Dtos;

namespace GDriveApi.Interfaces;

public interface IGoogleDriveService
{
    Task<GoogleDriveUploadResult> UploadFileAsync(IFormFile file, string fileName);
    Task DeleteFileAsync(string googleDriveFileId);
    string GetDirectUrl(string googleDriveFileId, string? contentType = null, string? extension = null);
}
