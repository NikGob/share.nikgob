#:project ../GDriveApi/GDriveApi.csproj

using GDriveApi.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var root = Directory.GetCurrentDirectory();

void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

var rawToken = new string('a', 64);
var expectedHash = "sha256:" + Convert.ToHexString(
    SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
Check(AuthToken.Hash(rawToken) == expectedHash, "Bearer tokens must be stored as SHA-256 hashes.");
Check(AuthToken.IsHashed(expectedHash), "Generated token hashes must be recognized.");
Check(!AuthToken.IsHashed("sha256:not-a-complete-hash"), "Malformed hash-like tokens must be migrated as plaintext.");
Check(AuthToken.Hash(expectedHash) != expectedHash, "Stored token hashes must not work as bearer tokens.");

var legacyUpload = BsonSerializer.Deserialize<UploadEntry>(new BsonDocument
{
    ["_id"] = ObjectId.GenerateNewId(),
    ["slug"] = "legacy"
});
Check(!legacyUpload.IsDeleting && legacyUpload.DeleteClaimedAt is null,
    "Legacy upload documents must work without a data migration.");

var filesController = File.ReadAllText(Path.Combine(root, "GDriveApi", "Controllers", "FilesController.cs"));
var collectionsController = File.ReadAllText(Path.Combine(root, "GDriveApi", "Controllers", "CollectionsController.cs"));
var fileManager = File.ReadAllText(Path.Combine(root, "GDriveApi", "Services", "FileManagerService.cs"));
var authMiddleware = File.ReadAllText(Path.Combine(root, "GDriveApi", "Middlewares", "AuthMiddleware.cs"));
var authInitializer = File.ReadAllText(Path.Combine(root, "GDriveApi", "Services", "AuthTokenInitializer.cs"));

Check(!Regex.IsMatch(filesController, @"\[HttpGet\]\s+public"), "GET /api/v1/files must stay removed.");
Check(collectionsController.Contains("int? page = null") && collectionsController.Contains("int? pageSize = null"),
    "Collection pagination must remain optional.");
Check(fileManager.Contains("if (page.HasValue || pageSize.HasValue)"), "Pagination must only run when requested.");
Check(fileManager.Contains("mongoDb.Collections"), "Stored collections must remain enabled.");
Check(!authMiddleware.Contains("Eq(t => t.Token, token)"), "Plaintext/pass-the-hash authentication must stay disabled.");
Check(authInitializer.Contains("Auth:InitialAdminToken"), "The first admin token must remain configurable.");
Check(!authInitializer.Contains("mongoDb.Uploads") && !authInitializer.Contains("mongoDb.Collections"),
    "Token migration must not read or write uploads or collections.");
Check(File.Exists(Path.Combine(root, "GDriveApi", "Models", "CollectionEntry.cs")),
    "The stored collection model must remain enabled.");

var project = XDocument.Load(Path.Combine(root, "GDriveApi", "GDriveApi.csproj"));
var mongoVersion = project.Descendants("PackageReference")
    .Single(e => (string?)e.Attribute("Include") == "MongoDB.Driver")
    .Attribute("Version")?.Value;
Check(Version.TryParse(mongoVersion, out var parsedMongoVersion) && parsedMongoVersion >= new Version(3, 10, 0),
    "MongoDB.Driver must stay on a vulnerability-fixed version.");

var dockerIgnore = File.ReadAllText(Path.Combine(root, ".dockerignore"));
Check(dockerIgnore.Contains("**/appsettings*.json") && dockerIgnore.Contains("**/google-credentials*.json"),
    "Docker must exclude real configuration files.");

var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "docker-image.yml"));
var actionRefs = Regex.Matches(workflow, @"uses:\s+[^\s@]+@([^\s#]+)");
Check(actionRefs.Count > 0 && actionRefs.All(m => Regex.IsMatch(m.Groups[1].Value, "^[0-9a-f]{40}$")),
    "GitHub Actions must stay pinned to immutable commits.");

Console.WriteLine("Critical checks passed.");
