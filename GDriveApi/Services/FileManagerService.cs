using System.Data;
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

    private static async Task TryRollbackAsync(Func<Task> rollback, string description)
    {
        try
        {
            await rollback();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Rollback] Failed to {description}: {ex.Message}");
        }
    }

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
        return string.IsNullOrWhiteSpace(slug) ? GenerateRandomSlug(slugLength) : slug.Trim();
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
        var collection = string.IsNullOrWhiteSpace(request.Collection) ? "default" : request.Collection.Trim();

        GoogleDriveUploadResult? gdResult = null;
        ShlinkResult? shlinkResult = null;
        UploadEntry? entry = null;
        var collectionIncremented = false;

        try
        {
            gdResult = await googleDriveService.UploadFileAsync(request.File, displayName, skipImageServing);
            shlinkResult = await shlinkService.CreateShortUrlAsync(
                gdResult.LongUrl, slug, request.Title, request.Crawlable);

            entry = new UploadEntry
            {
                Slug = shlinkResult.ShortCode,
                FileName = displayName,
                ContentType = request.File.ContentType,
                FileSize = request.File.Length,
                GoogleDriveFileId = gdResult.GoogleDriveFileId,
                ShortUrl = shlinkResult.ShortUrl,
                LongUrl = shlinkResult.LongUrl,
                Collection = collection,
                Title = shlinkResult.Title,
                Crawlable = shlinkResult.Crawlable,
                OwnerId = currentUser.Id,
                SkipImageServing = skipImageServing,
                CreatedAt = DateTime.UtcNow
            };

            await mongoDb.Uploads.InsertOneAsync(entry);
            await mongoDb.Collections.UpdateOneAsync(
                Builders<CollectionEntry>.Filter.Eq(c => c.Name, entry.Collection),
                Builders<CollectionEntry>.Update
                    .Inc(c => c.FileCount, 1)
                    .Set(c => c.UpdatedAt, DateTime.UtcNow)
                    .SetOnInsert(c => c.CreatedAt, DateTime.UtcNow),
                new UpdateOptions { IsUpsert = true });
            collectionIncremented = true;

            return ToDto(entry, currentUser.Username);
        }
        catch
        {
            if (entry is not null)
                await TryRollbackAsync(
                    () => mongoDb.Uploads.DeleteOneAsync(Builders<UploadEntry>.Filter.Eq(e => e.Id, entry.Id)),
                    $"remove upload record '{entry.Slug}'");
            if (collectionIncremented && entry is not null)
                await TryRollbackAsync(async () =>
                {
                    await mongoDb.Collections.UpdateOneAsync(
                        Builders<CollectionEntry>.Filter.Eq(c => c.Name, entry.Collection),
                        Builders<CollectionEntry>.Update.Inc(c => c.FileCount, -1));
                    await mongoDb.Collections.DeleteOneAsync(
                        Builders<CollectionEntry>.Filter.Eq(c => c.Name, entry.Collection)
                        & Builders<CollectionEntry>.Filter.Lte(c => c.FileCount, 0));
                }, $"restore collection '{entry.Collection}'");
            if (shlinkResult is not null)
                await TryRollbackAsync(
                    () => shlinkService.DeleteShortUrlAsync(shlinkResult.ShortCode),
                    $"delete short URL '{shlinkResult.ShortCode}'");
            if (gdResult is not null)
                await TryRollbackAsync(
                    () => googleDriveService.DeleteFileAsync(gdResult.GoogleDriveFileId),
                    $"delete Google Drive file '{gdResult.GoogleDriveFileId}'");

            throw;
        }
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

        var claimTime = DateTime.UtcNow;
        // ponytail: five minutes is the stale-claim ceiling; make it configurable only for slower external deletes.
        var staleClaim = claimTime.AddMinutes(-5);
        var availableForDelete = Builders<UploadEntry>.Filter.Ne(e => e.IsDeleting, true)
            | Builders<UploadEntry>.Filter.Lt(e => e.DeleteClaimedAt, staleClaim)
            | Builders<UploadEntry>.Filter.Eq(e => e.DeleteClaimedAt, null);
        var claimResult = await mongoDb.Uploads.UpdateOneAsync(
            Builders<UploadEntry>.Filter.Eq(e => e.Id, entry.Id)
            & Builders<UploadEntry>.Filter.Eq(e => e.Slug, slug)
            & availableForDelete,
            Builders<UploadEntry>.Update
                .Set(e => e.IsDeleting, true)
                .Set(e => e.DeleteClaimedAt, claimTime));

        if (claimResult.MatchedCount == 0)
            throw new DBConcurrencyException("File is already being modified or deleted. Retry the request.");

        try
        {
            await shlinkService.DeleteShortUrlAsync(entry.Slug);
            await googleDriveService.DeleteFileAsync(entry.GoogleDriveFileId);
            var deleteResult = await mongoDb.Uploads.DeleteOneAsync(
                Builders<UploadEntry>.Filter.Eq(e => e.Id, entry.Id));

            if (deleteResult.DeletedCount == 1)
            {
                await mongoDb.Collections.UpdateOneAsync(
                    Builders<CollectionEntry>.Filter.Eq(c => c.Name, entry.Collection),
                    Builders<CollectionEntry>.Update
                        .Inc(c => c.FileCount, -1)
                        .Set(c => c.UpdatedAt, DateTime.UtcNow));
                await mongoDb.Collections.DeleteOneAsync(
                    Builders<CollectionEntry>.Filter.Eq(c => c.Name, entry.Collection)
                    & Builders<CollectionEntry>.Filter.Lte(c => c.FileCount, 0));
            }
        }
        catch
        {
            await TryRollbackAsync(
                () => mongoDb.Uploads.UpdateOneAsync(
                    Builders<UploadEntry>.Filter.Eq(e => e.Id, entry.Id)
                    & Builders<UploadEntry>.Filter.Eq(e => e.DeleteClaimedAt, claimTime),
                    Builders<UploadEntry>.Update
                        .Set(e => e.IsDeleting, false)
                        .Unset(e => e.DeleteClaimedAt)),
                $"release deletion claim for '{slug}'");
            throw;
        }
    }

    public async Task<FileDto> UpdateFileAsync(string slug, UpdateFileRequest request, AuthToken currentUser)
    {
        var entry = await mongoDb.Uploads
            .Find(Builders<UploadEntry>.Filter.Eq(e => e.Slug, slug))
            .FirstOrDefaultAsync()
            ?? throw new KeyNotFoundException($"File with slug '{slug}' not found.");

        EnforceOwnership(entry, currentUser);
        if (entry.IsDeleting)
            throw new DBConcurrencyException("File is being deleted. Retry the request.");

        var newSlug = string.IsNullOrWhiteSpace(request.NewSlug) ? null : request.NewSlug.Trim();

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

        var effectiveLongUrl = newLongUrl ?? entry.LongUrl;

        if (newSlug is not null && newSlug != slug)
        {
            var shlinkResult = await shlinkService.CreateShortUrlAsync(
                effectiveLongUrl,
                newSlug,
                request.Title ?? entry.Title,
                request.Crawlable ?? entry.Crawlable);

            try
            {
                var update = Builders<UploadEntry>.Update
                    .Set(e => e.Slug, shlinkResult.ShortCode)
                    .Set(e => e.ShortUrl, shlinkResult.ShortUrl)
                    .Set(e => e.Title, shlinkResult.Title)
                    .Set(e => e.Crawlable, shlinkResult.Crawlable);

                if (skipImageServingChanged)
                {
                    update = update
                        .Set(e => e.SkipImageServing, request.SkipImageServing!.Value)
                        .Set(e => e.LongUrl, effectiveLongUrl);
                }

                var updateFilter = Builders<UploadEntry>.Filter.Eq(e => e.Id, entry.Id)
                    & Builders<UploadEntry>.Filter.Eq(e => e.Slug, slug)
                    & Builders<UploadEntry>.Filter.Ne(e => e.IsDeleting, true)
                    & Builders<UploadEntry>.Filter.Eq(e => e.Title, entry.Title)
                    & Builders<UploadEntry>.Filter.Eq(e => e.Crawlable, entry.Crawlable);

                if (skipImageServingChanged)
                {
                    updateFilter &= Builders<UploadEntry>.Filter.Eq(e => e.LongUrl, entry.LongUrl)
                        & Builders<UploadEntry>.Filter.Eq(e => e.SkipImageServing, entry.SkipImageServing);
                }

                var updateResult = await mongoDb.Uploads.UpdateOneAsync(updateFilter, update);

                if (updateResult.MatchedCount == 0)
                    throw new DBConcurrencyException("File was modified concurrently. Retry the request.");
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
                    var updateFilter = Builders<UploadEntry>.Filter.Eq(e => e.Id, entry.Id)
                        & Builders<UploadEntry>.Filter.Eq(e => e.Slug, slug)
                        & Builders<UploadEntry>.Filter.Ne(e => e.IsDeleting, true);

                    if (request.Title != null)
                        updateFilter &= Builders<UploadEntry>.Filter.Eq(e => e.Title, entry.Title);
                    if (request.Crawlable.HasValue)
                        updateFilter &= Builders<UploadEntry>.Filter.Eq(e => e.Crawlable, entry.Crawlable);
                    if (skipImageServingChanged)
                    {
                        updateFilter &= Builders<UploadEntry>.Filter.Eq(e => e.LongUrl, entry.LongUrl)
                            & Builders<UploadEntry>.Filter.Eq(e => e.SkipImageServing, entry.SkipImageServing);
                    }

                    try
                    {
                        var updateResult = await mongoDb.Uploads.UpdateOneAsync(
                            updateFilter, Builders<UploadEntry>.Update.Combine(updates));

                        if (updateResult.MatchedCount == 0)
                            throw new DBConcurrencyException("File was modified concurrently. Retry the request.");
                    }
                    catch
                    {
                        try
                        {
                            var current = await mongoDb.Uploads
                                .Find(Builders<UploadEntry>.Filter.Eq(e => e.Id, entry.Id))
                                .FirstOrDefaultAsync();

                            if (current is { IsDeleting: false } && current.Slug == slug)
                            {
                                await shlinkService.UpdateShortUrlAsync(
                                    slug,
                                    newLongUrl is not null ? current.LongUrl : null,
                                    request.Title != null ? current.Title ?? string.Empty : null,
                                    request.Crawlable.HasValue ? current.Crawlable ?? false : null);
                            }
                        }
                        catch (Exception rollbackEx)
                        {
                            Console.WriteLine(
                                $"[Shlink] Failed to resync short URL '{slug}' after Mongo update error: {rollbackEx.Message}");
                        }

                        throw;
                    }
                }
            }
        }

        return await GetFileInfoAsync(newSlug ?? slug);
    }

    public async Task<List<CollectionDto>> GetCollectionsAsync()
    {
        var entries = await mongoDb.Collections
            .Find(Builders<CollectionEntry>.Filter.Empty)
            .SortByDescending(c => c.UpdatedAt)
            .ToListAsync();

        return entries.Select(c => new CollectionDto
        {
            Name = c.Name,
            FileCount = c.FileCount,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        }).ToList();
    }

    public async Task<List<FileDto>> GetFilesByCollectionAsync(string collection, int? page, int? pageSize)
    {
        IFindFluent<UploadEntry, UploadEntry> query = mongoDb.Uploads
            .Find(Builders<UploadEntry>.Filter.Eq(e => e.Collection, collection)
                & Builders<UploadEntry>.Filter.Ne(e => e.IsDeleting, true))
            .SortByDescending(e => e.CreatedAt);

        if (page.HasValue || pageSize.HasValue)
        {
            var effectivePage = page ?? 1;
            var effectivePageSize = pageSize ?? 100;
            query = query.Skip((effectivePage - 1) * effectivePageSize).Limit(effectivePageSize);
        }

        var entries = await query.ToListAsync();
        if (entries.Count == 0)
            return [];

        var ownerIds = entries.Select(e => e.OwnerId).Distinct().ToList();
        var users = await mongoDb.AuthTokens
            .Find(Builders<AuthToken>.Filter.In(t => t.Id, ownerIds))
            .ToListAsync();
        var usernames = users.ToDictionary(u => u.Id, u => u.Username);

        return entries
            .Select(e => ToDto(e, usernames.GetValueOrDefault(e.OwnerId, "unknown")))
            .ToList();
    }
}
