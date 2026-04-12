using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GDriveApi.Dtos;
using GDriveApi.Interfaces;
using GDriveApi.Models;
using GDriveApi.Requests;
using MongoDB.Driver;

namespace GDriveApi.Services;

public class FileManagerService(
    IGoogleDriveService googleDriveService,
    IShlinkService shlinkService,
    MongoDbService mongoDb) : IFileManagerService
{
    private const string SlugChars = "abcdefghijklmnopqrstuvwxyz0123456789";

    private static string GenerateRandomSlug(int length)
    {
        if (length < 3) length = 3;
        if (length > 64) length = 64;

        return string.Create(length, 0, static (span, _) =>
        {
            Span<byte> random = stackalloc byte[span.Length];
            RandomNumberGenerator.Fill(random);
            for (var i = 0; i < span.Length; i++)
                span[i] = SlugChars[random[i] % SlugChars.Length];
        });
    }

    private static string ResolveSlug(string? slug, int slugLength)
    {
        return string.IsNullOrWhiteSpace(slug) ? GenerateRandomSlug(slugLength) : slug;
    }

    private FileDto ToDto(UploadEntry e, string? uploaderName = null) => new()
    {
        Id = e.Id.ToString(),
        Slug = e.Slug,
        FileName = e.FileName,
        ContentType = e.ContentType,
        FileSize = e.FileSize,
        FileSizeFormatted = FileDto.FormatFileSize(e.FileSize),
        GoogleDriveFileId = e.GoogleDriveFileId,
        LongUrl = e.LongUrl,
        ShortUrl = e.ShortUrl,
        Collection = e.Collection,
        Title = e.Title,
        Crawlable = e.Crawlable,
        SkipImageServing = e.SkipImageServing,
        UploadedBy = uploaderName ?? string.Empty,
        CreatedAt = e.CreatedAt
    };

    private async Task<string> ResolveUsername(MongoDB.Bson.ObjectId ownerId)
    {
        var user = await mongoDb.AuthTokens
            .Find(Builders<AuthToken>.Filter.Eq(t => t.Id, ownerId))
            .FirstOrDefaultAsync();
        return user?.Username ?? "unknown";
    }

    private async Task<FileDto> ToDtoWithUsername(UploadEntry e)
    {
        var username = await ResolveUsername(e.OwnerId);
        return ToDto(e, username);
    }

    private static void EnforceOwnership(UploadEntry entry, AuthToken user)
    {
        if (user.Role != "admin" && entry.OwnerId != user.Id)
            throw new UnauthorizedAccessException("You can only modify your own files.");
    }

    private static readonly Regex SafeNameRegex = new(
        @"[^\w\d\s\-_.а-яА-ЯёЁ]", RegexOptions.Compiled);

    private static string SanitizeFileName(string name)
    {
        name = name.Replace(' ', '_');
        name = SafeNameRegex.Replace(name, "");
        name = Regex.Replace(name, @"_{2,}", "_");
        name = name.Trim('_', '.');
        return string.IsNullOrEmpty(name) ? "file" : name;
    }

    private static string ResolveDisplayName(UploadFileRequest request)
    {
        if (request.RenameFile == true && !string.IsNullOrWhiteSpace(request.CustomFileName))
        {
            var name = request.CustomFileName;
            var ext = Path.GetExtension(request.File.FileName);
            if (!string.IsNullOrEmpty(ext) && !name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                name += ext;
            return name;
        }

        var baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(request.File.FileName));
        var originalExt = Path.GetExtension(request.File.FileName);
        return baseName + originalExt;
    }

    public async Task<FileDto> UploadFileAsync(UploadFileRequest request, AuthToken currentUser)
    {
        var slug = ResolveSlug(request.Slug, request.SlugLength);
        var displayName = ResolveDisplayName(request);

        var skipImageServing = request.SkipImageServing ?? false;
        var gdResult = await googleDriveService.UploadFileAsync(request.File, displayName, skipImageServing);

        var shlinkResult = await shlinkService.CreateShortUrlAsync(
            gdResult.LongUrl, slug, request.Title, request.Crawlable);

        var entry = new UploadEntry
        {
            Slug = shlinkResult.ShortCode,
            FileName = displayName,
            ContentType = request.File.ContentType,
            FileSize = request.File.Length,
            GoogleDriveFileId = gdResult.GoogleDriveFileId,
            ShortUrl = shlinkResult.ShortUrl,
            LongUrl = shlinkResult.LongUrl,
            Collection = request.Collection,
            Title = shlinkResult.Title,
            Crawlable = shlinkResult.Crawlable,
            OwnerId = currentUser.Id,
            SkipImageServing = skipImageServing,
            CreatedAt = DateTime.UtcNow
        };

        await mongoDb.Uploads.InsertOneAsync(entry);

        return ToDto(entry, currentUser.Username);
    }

    public async Task<FileDto> GetFileInfoAsync(string slug)
    {
        var entry = await mongoDb.Uploads
            .Find(Builders<UploadEntry>.Filter.Eq(e => e.Slug, slug))
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException($"File with slug '{slug}' not found.");

        return await ToDtoWithUsername(entry);
    }

    public async Task DeleteFileAsync(string slug, AuthToken currentUser)
    {
        var entry = await mongoDb.Uploads
            .Find(Builders<UploadEntry>.Filter.Eq(e => e.Slug, slug))
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException($"File with slug '{slug}' not found.");

        EnforceOwnership(entry, currentUser);

        await googleDriveService.DeleteFileAsync(entry.GoogleDriveFileId);

        try
        {
            await shlinkService.DeleteShortUrlAsync(entry.Slug);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Shlink] Failed to delete short URL '{slug}': {ex.Message}");
        }

        await mongoDb.Uploads.DeleteOneAsync(
            Builders<UploadEntry>.Filter.Eq(e => e.Id, entry.Id));
    }

    public async Task<FileDto> UpdateFileAsync(string slug, UpdateFileRequest request, AuthToken currentUser)
    {
        var entry = await mongoDb.Uploads
            .Find(Builders<UploadEntry>.Filter.Eq(e => e.Slug, slug))
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException($"File with slug '{slug}' not found.");

        EnforceOwnership(entry, currentUser);

        if (!string.IsNullOrWhiteSpace(request.NewSlug) && request.NewSlug != slug)
        {
            var shlinkResult = await shlinkService.CreateShortUrlAsync(
                entry.LongUrl,
                request.NewSlug,
                request.Title ?? entry.Title,
                request.Crawlable ?? entry.Crawlable);

            try
            {
                var update = Builders<UploadEntry>.Update
                    .Set(e => e.Slug, shlinkResult.ShortCode)
                    .Set(e => e.ShortUrl, shlinkResult.ShortUrl)
                    .Set(e => e.Title, shlinkResult.Title)
                    .Set(e => e.Crawlable, shlinkResult.Crawlable);

                await mongoDb.Uploads.UpdateOneAsync(
                    Builders<UploadEntry>.Filter.Eq(e => e.Id, entry.Id), update);
            }
            catch
            {
                try
                {
                    await shlinkService.DeleteShortUrlAsync(shlinkResult.ShortCode);
                }
                catch (Exception rollbackEx)
                {
                    Console.WriteLine(
                        $"[Shlink] Failed to rollback new short URL '{shlinkResult.ShortCode}' after Mongo update error: {rollbackEx.Message}");
                }

                throw;
            }

            try
            {
                await shlinkService.DeleteShortUrlAsync(slug);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Shlink] Failed to delete old short URL '{slug}' after slug change: {ex.Message}");
            }
        }
        else
        {
            var skipImageServingChanged = request.SkipImageServing.HasValue
                && request.SkipImageServing.Value != entry.SkipImageServing
                && entry.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

            string? newLongUrl = null;
            if (skipImageServingChanged)
            {
                var ext = Path.GetExtension(entry.FileName);
                newLongUrl = googleDriveService.GetDirectUrl(
                    entry.GoogleDriveFileId, entry.ContentType, ext, request.SkipImageServing!.Value);
            }

            bool needsShlinkUpdate = request.Title != null || request.Crawlable.HasValue || newLongUrl != null;

            if (needsShlinkUpdate)
            {
                var shlinkResult = await shlinkService.UpdateShortUrlAsync(
                    slug, newLongUrl, request.Title, request.Crawlable);

                var updates = new List<UpdateDefinition<UploadEntry>>();

                if (request.Title != null)
                    updates.Add(Builders<UploadEntry>.Update.Set(e => e.Title, shlinkResult.Title));
                if (request.Crawlable.HasValue)
                    updates.Add(Builders<UploadEntry>.Update.Set(e => e.Crawlable, shlinkResult.Crawlable));
                if (skipImageServingChanged)
                {
                    updates.Add(Builders<UploadEntry>.Update.Set(e => e.SkipImageServing, request.SkipImageServing!.Value));
                    updates.Add(Builders<UploadEntry>.Update.Set(e => e.LongUrl, shlinkResult.LongUrl));
                }

                if (updates.Count > 0)
                {
                    await mongoDb.Uploads.UpdateOneAsync(
                        Builders<UploadEntry>.Filter.Eq(e => e.Id, entry.Id),
                        Builders<UploadEntry>.Update.Combine(updates));
                }
            }
        }

        return await GetFileInfoAsync(request.NewSlug ?? slug);
    }

    public async Task<List<string>> GetCollectionsAsync()
    {
        return await mongoDb.Uploads
            .DistinctAsync(e => e.Collection, Builders<UploadEntry>.Filter.Empty)
            .Result.ToListAsync();
    }

    public async Task<List<FileDto>> GetFilesByCollectionAsync(string collection)
    {
        var entries = await mongoDb.Uploads
            .Find(Builders<UploadEntry>.Filter.Eq(e => e.Collection, collection))
            .SortByDescending(e => e.CreatedAt)
            .ToListAsync();

        var tasks = entries.Select(ToDtoWithUsername);
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }
}
