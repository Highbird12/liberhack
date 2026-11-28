using System.Collections.Concurrent;
using BookLibrary.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Http;

namespace BookLibrary.Tests.Infrastructure;

// Keeps "uploaded" files in memory instead of Supabase or the disk.
public class FakeFileStorage : IFileStorage
{
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();
    public string Description => "in-memory test storage";

    public async Task<string> SaveAsync(IFormFile file, string folder, CancellationToken ct = default)
    {
        var url = $"https://storage.test/{folder}/{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        Files[url] = ms.ToArray();
        return url;
    }

    public Task DeleteAsync(string? url, CancellationToken ct = default)
    {
        if (url != null) Files.TryRemove(url, out _);
        return Task.CompletedTask;
    }
}

public static class TestFiles
{
    public const string DocxType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    // A 1x1 PNG image.
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray();

    public static readonly byte[] NotAPdf = "this is not a pdf"u8.ToArray();

    // A Word document containing one paragraph of text.
    public static byte[] Docx(string text)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text(text)))));
        }
        return ms.ToArray();
    }
}
