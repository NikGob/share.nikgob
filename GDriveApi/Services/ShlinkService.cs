using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GDriveApi.Dtos;
using GDriveApi.Interfaces;

namespace GDriveApi.Services;

public class ShlinkService : IShlinkService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _domain;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ShlinkService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _baseUrl = (configuration["Shlink:BaseUrl"]
            ?? throw new InvalidOperationException("Shlink:BaseUrl is not configured.")).TrimEnd('/');

        var apiKey = configuration["Shlink:ApiKey"]
            ?? throw new InvalidOperationException("Shlink:ApiKey is not configured.");

        _domain = configuration["Shlink:Domain"] ?? string.Empty;

        _httpClient = httpClientFactory.CreateClient("Shlink");
        _httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ShlinkResult> CreateShortUrlAsync(string longUrl, string customSlug, string? title, bool? crawlable)
    {
        var body = new Dictionary<string, object?>
        {
            ["longUrl"] = longUrl,
            ["customSlug"] = customSlug,
            ["findIfExists"] = false
        };

        if (crawlable.HasValue)
            body["crawlable"] = crawlable.Value;

        if (!string.IsNullOrWhiteSpace(_domain))
            body["domain"] = _domain;

        if (!string.IsNullOrWhiteSpace(title))
            body["title"] = title;

        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/rest/v3/short-urls", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Shlink create failed ({response.StatusCode}): {responseBody}");

        return ParseShlinkResponse(responseBody);
    }

    public async Task<ShlinkResult> UpdateShortUrlAsync(string shortCode, string? longUrl, string? title, bool? crawlable)
    {
        var body = new Dictionary<string, object?>();

        if (longUrl != null) body["longUrl"] = longUrl;
        if (title != null) body["title"] = title;
        if (crawlable.HasValue) body["crawlable"] = crawlable.Value;

        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = BuildShortCodeUrl(shortCode);
        var response = await _httpClient.PatchAsync(url, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Shlink update failed ({response.StatusCode}): {responseBody}");

        return ParseShlinkResponse(responseBody);
    }

    public async Task DeleteShortUrlAsync(string shortCode)
    {
        var url = BuildShortCodeUrl(shortCode);
        var response = await _httpClient.DeleteAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Shlink delete failed ({response.StatusCode}): {responseBody}");
        }
    }

    public async Task<ShlinkResult> GetShortUrlAsync(string shortCode)
    {
        var url = BuildShortCodeUrl(shortCode);
        var response = await _httpClient.GetAsync(url);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Shlink get failed ({response.StatusCode}): {responseBody}");

        return ParseShlinkResponse(responseBody);
    }

    public async Task<ShlinkResult> ChangeSlugAsync(string oldSlug, string longUrl, string newSlug, string? title, bool? crawlable)
    {
        await DeleteShortUrlAsync(oldSlug);
        return await CreateShortUrlAsync(longUrl, newSlug, title, crawlable);
    }

    private string BuildShortCodeUrl(string shortCode)
    {
        var url = $"{_baseUrl}/rest/v3/short-urls/{shortCode}";
        if (!string.IsNullOrWhiteSpace(_domain))
            url += $"?domain={_domain}";
        return url;
    }

    private static ShlinkResult ParseShlinkResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new ShlinkResult
        {
            ShortCode = root.GetProperty("shortCode").GetString() ?? string.Empty,
            ShortUrl = root.GetProperty("shortUrl").GetString() ?? string.Empty,
            LongUrl = root.GetProperty("longUrl").GetString() ?? string.Empty,
            Title = root.TryGetProperty("title", out var t) ? t.GetString() : null,
            Crawlable = root.TryGetProperty("crawlable", out var c) && c.GetBoolean()
        };
    }
}
