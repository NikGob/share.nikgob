using GDriveApi.Dtos;

namespace GDriveApi.Interfaces;

public interface IGoogleDriveService
{
    Task<GoogleDriveUploadResult> UploadFileAsync(IFormFile file, string fileName, bool noCompression = false);
    Task DeleteFileAsync(string googleDriveFileId);
    string GetDirectUrl(string googleDriveFileId, string? contentType = null, string? extension = null, bool noCompression = false);
}
