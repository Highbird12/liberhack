using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BookLibrary.Services;

// Where admin uploads (book covers and English PDFs) are kept.
// Files get random names, so user file names never become paths and uploads can't overwrite each other.
public interface IFileStorage
{
    // Shown in the startup log.
    string Description { get; }

    // Saves the file under folder ("covers" or "pdfs") and returns the URL to store on the book.
    // Throws FileStorageException with a message that can be shown to the admin.
    Task<string> SaveAsync(IFormFile file, string folder, CancellationToken ct = default);

    // Deletes a file that SaveAsync returned. URLs it did not create (such as the seeded
    // /covers and /pdfs files) are left alone. Never throws; failures are logged.
    Task DeleteAsync(string? url, CancellationToken ct = default);
}

public class FileStorageException(string message) : Exception(message);

// Default when no cloud storage is configured: files go to wwwroot/uploads on this machine.
// Fine on your own PC; on a host the container's disk is wiped on every deploy.
public class LocalFileStorage(IWebHostEnvironment env) : IFileStorage
{
    public string Description => "local folder wwwroot/uploads (lost when a hosted container is redeployed)";

    public async Task<string> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var dir = Path.Combine(env.WebRootPath, "uploads", folder);
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}{ext}";
        await using (var fs = File.Create(Path.Combine(dir, name)))
            await file.CopyToAsync(fs, ct);
        return $"/uploads/{folder}/{name}";
    }

    public Task DeleteAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("/uploads/")) return Task.CompletedTask;
        var root = Path.GetFullPath(Path.Combine(env.WebRootPath, "uploads")) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(env.WebRootPath, url.TrimStart('/')));
        if (path.StartsWith(root) && File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}

// Supabase Storage, used when Storage:Supabase:Url and Storage:Supabase:Key are set.
// Files live in a public bucket, so the browser loads them straight from Supabase and they
// survive redeploys. The key must be a secret (or legacy service_role) key and stay on the server:
// it bypasses all storage access rules.
public class SupabaseFileStorage : IFileStorage
{
    readonly HttpClient _http;
    readonly ILogger<SupabaseFileStorage> _log;
    readonly string _baseUrl;
    readonly string _bucket;

    public static bool IsConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config["Storage:Supabase:Url"]) &&
        !string.IsNullOrWhiteSpace(config["Storage:Supabase:Key"]);

    public SupabaseFileStorage(HttpClient http, IConfiguration config, ILogger<SupabaseFileStorage> log)
    {
        _baseUrl = config["Storage:Supabase:Url"]!.Trim().TrimEnd('/');
        _bucket = string.IsNullOrWhiteSpace(config["Storage:Supabase:Bucket"]) ? "uploads" : config["Storage:Supabase:Bucket"]!.Trim();
        var key = config["Storage:Supabase:Key"]!.Trim();

        http.BaseAddress = new Uri(_baseUrl + "/storage/v1/");
        // Secret keys (sb_secret_...) are not JWTs and must only be sent in the apikey header.
        // Legacy service_role keys are JWTs (they start with "eyJ") and are also sent as a bearer token.
        http.DefaultRequestHeaders.Add("apikey", key);
        if (key.StartsWith("eyJ", StringComparison.Ordinal))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        _http = http;
        _log = log;
    }

    string PublicPrefix => $"{_baseUrl}/storage/v1/object/public/{_bucket}/";

    public string Description => $"Supabase Storage bucket \"{_bucket}\" at {_baseUrl}";

    // Creates the bucket (public) if it does not exist yet, and warns if it exists but is private,
    // because then covers and PDFs would not load in the browser.
    public async Task EnsureBucketAsync(CancellationToken ct = default)
    {
        try
        {
            // A plain string body has a fixed length (PostAsJsonAsync would stream it chunked).
            using var json = new StringContent(JsonSerializer.Serialize(new { id = _bucket, name = _bucket, @public = true }),
                System.Text.Encoding.UTF8, "application/json");
            using (var create = await _http.PostAsync("bucket", json, ct))
            {
                if (create.IsSuccessStatusCode)
                {
                    _log.LogInformation("Created public storage bucket {Bucket}.", _bucket);
                    return;
                }
                var body = await create.Content.ReadAsStringAsync(ct);
                if (create.StatusCode != HttpStatusCode.Conflict && !body.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    _log.LogWarning("Could not create storage bucket {Bucket}: {Status} {Body}. Uploads will fail until this is fixed.",
                        _bucket, (int)create.StatusCode, Short(body));
                    return;
                }
            }

            using var get = await _http.GetAsync($"bucket/{Uri.EscapeDataString(_bucket)}", ct);
            if (!get.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await get.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("public", out var isPublic) && isPublic.ValueKind == JsonValueKind.False)
                _log.LogWarning("Storage bucket {Bucket} is private, so uploaded covers and PDFs will not load. " +
                                "Make it public in Supabase (Storage > bucket > Edit bucket).", _bucket);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _log.LogWarning(ex, "Could not check storage bucket {Bucket}.", _bucket);
        }
    }

    public async Task<string> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
    {
        var path = $"{folder}/{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        try
        {
            await using var stream = file.OpenReadStream();
            using var content = new StreamContent(stream);
            content.Headers.ContentType = MediaTypeHeaderValue.TryParse(file.ContentType, out var type)
                ? type : new MediaTypeHeaderValue("application/octet-stream");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"object/{Uri.EscapeDataString(_bucket)}/{path}") { Content = content };
            request.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromHours(1) };

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _log.LogWarning("Upload to storage bucket {Bucket} failed: {Status} {Body}", _bucket, (int)response.StatusCode, Short(body));
                throw new FileStorageException(response.StatusCode == HttpStatusCode.RequestEntityTooLarge || body.Contains("size", StringComparison.OrdinalIgnoreCase)
                    ? "The file is larger than the storage allows. Try a smaller file."
                    : $"The file could not be stored (storage answered {(int)response.StatusCode}). Please try again.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning(ex, "Could not reach storage to upload a file.");
            throw new FileStorageException("The file storage could not be reached. Please try again.");
        }
        return PublicPrefix + path;
    }

    public async Task DeleteAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith(PublicPrefix, StringComparison.Ordinal)) return;
        var path = url[PublicPrefix.Length..];
        try
        {
            using var response = await _http.DeleteAsync($"object/{Uri.EscapeDataString(_bucket)}/{path}", ct);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                _log.LogWarning("Could not delete {Path} from storage: {Status}", path, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning(ex, "Could not reach storage to delete {Path}.", path);
        }
    }

    static string Short(string s) => s.Length <= 300 ? s : s[..300] + "...";
}
