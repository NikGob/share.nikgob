using GDriveApi.Dtos;
using GDriveApi.Models;
using GDriveApi.Requests;

namespace GDriveApi.Interfaces;

public interface IFileManagerService
{
    Task<FileDto> UploadFileAsync(UploadFileRequest request, AuthToken currentUser);
    Task<FileDto> GetFileInfoAsync(string slug);
    Task DeleteFileAsync(string slug, AuthToken currentUser);
    Task<FileDto> UpdateFileAsync(string slug, UpdateFileRequest request, AuthToken currentUser);
    Task<List<CollectionDto>> GetCollectionsAsync();
    Task<List<FileDto>> GetFilesByCollectionAsync(string collection, int? page, int? pageSize);
}
