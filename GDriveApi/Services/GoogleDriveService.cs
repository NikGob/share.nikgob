using GDriveApi.Dtos;
using GDriveApi.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;

namespace GDriveApi.Services;

public class GoogleDriveService : IGoogleDriveService
{
    private readonly string _folderId;
    private readonly string _baseDownloadUrl;
    private readonly string _imageDownloadUrl;
    private readonly DriveService _driveService;

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mov", ".wmv", ".flv", ".mkv", ".webm",
        ".mpeg", ".mpg", ".3gp", ".ogv", ".m4v", ".ts", ".vob",
        ".3gpp", ".3gpp2", ".mts", ".m2ts"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg",
        ".tiff", ".tif", ".ico", ".heic", ".heif", ".avif", ".jfif"
    };

    public GoogleDriveService(IConfiguration configuration)
    {
        _folderId = configuration["Google:FolderId"]
            ?? throw new InvalidOperationException("Google:FolderId is not configured.");
        _baseDownloadUrl = configuration["Google:BaseDownloadUrl"]
            ?? throw new InvalidOperationException("Google:BaseDownloadUrl is not configured.");
        _imageDownloadUrl = configuration["Google:ImageDownloadUrl"]
            ?? throw new InvalidOperationException("Google:ImageDownloadUrl is not configured.");

        var clientId = configuration["Google:ClientId"]
            ?? throw new InvalidOperationException("Google:ClientId is not configured.");
        var clientSecret = configuration["Google:ClientSecret"]
            ?? throw new InvalidOperationException("Google:ClientSecret is not configured.");
        var refreshToken = configuration["Google:RefreshToken"]
            ?? throw new InvalidOperationException("Google:RefreshToken is not configured.");

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            },
            Scopes = [DriveService.ScopeConstants.DriveFile]
        });

        var credential = new UserCredential(flow, "user", new TokenResponse
        {
            RefreshToken = refreshToken
        });

        _driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "GDriveApi"
        });
    }

    public async Task<GoogleDriveUploadResult> UploadFileAsync(IFormFile file, string displayName, bool noCompression = false)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || VideoExtensions.Contains(extension))
        {
            throw new InvalidOperationException(
                "Video files are not allowed. Google Drive opens them with a different endpoint.");
        }

        var fileMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = displayName,
            MimeType = file.ContentType,
            Parents = [_folderId]
        };

        FilesResource.CreateMediaUpload request;
        await using (var fileStream = file.OpenReadStream())
        {
            request = _driveService.Files.Create(fileMetadata, fileStream, file.ContentType);
            request.Fields = "id";
            request.SupportsAllDrives = true;
            var uploadResult = await request.UploadAsync();

            if (uploadResult.Status != UploadStatus.Completed || request.ResponseBody?.Id == null)
                throw new InvalidOperationException(
                    $"File upload failed: {uploadResult.Exception?.Message ?? "Unknown error"}");
        }

        var fileId = request.ResponseBody!.Id;

        var permission = new Google.Apis.Drive.v3.Data.Permission
        {
            Role = "reader",
            Type = "anyone"
        };
        await _driveService.Permissions.Create(permission, fileId).ExecuteAsync();

        var longUrl = GetDirectUrl(fileId, file.ContentType, Path.GetExtension(file.FileName), noCompression);
        return new GoogleDriveUploadResult(fileId, longUrl);
    }

    public async Task DeleteFileAsync(string googleDriveFileId)
    {
        try
        {
            await _driveService.Files.Delete(googleDriveFileId).ExecuteAsync();
        }
        catch (Google.GoogleApiException e) when (e.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine($"[GoogleDrive] File {googleDriveFileId} not found, skipping delete.");
        }
    }

    private static bool IsImage(string contentType, string extension)
    {
        return contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
               || ImageExtensions.Contains(extension);
    }

    public string GetDirectUrl(string googleDriveFileId, string? contentType = null, string? extension = null, bool noCompression = false)
    {
        var isImage = contentType != null && extension != null && IsImage(contentType, extension);
        var baseUrl = isImage && !noCompression ? _imageDownloadUrl : _baseDownloadUrl;
        return $"{baseUrl}{googleDriveFileId}";
    }
}
