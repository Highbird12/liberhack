namespace BookLibrary.Models;

// Stored as numbers: only add new values at the end.
// Replaced = was approved, then a newer translation of the same book was approved.
public enum TranslationStatus { Pending, Approved, Rejected, Replaced }

public class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Nationality { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? CoverUrl { get; set; }
    public string? EnglishPdf { get; set; }
    public List<Translation> Translations { get; set; } = new();
}

public class Translation
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public Book? Book { get; set; }
    public string TranslatorName { get; set; } = "";
    // Identity user id of the submitter. Null for translations submitted before this field existed.
    public string? SubmittedByUserId { get; set; }
    public string Text { get; set; } = "";
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public TranslationStatus Status { get; set; } = TranslationStatus.Pending;
    // When an admin approved or rejected it.
    public DateTime? DecidedAt { get; set; }
    // Optional note from the admin when rejecting, shown to the translator.
    public string? RejectionReason { get; set; }
}
