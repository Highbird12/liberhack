namespace BookLibrary.Models;

public enum TranslationStatus { Pending, Approved, Rejected }

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
    public string Text { get; set; } = "";
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public TranslationStatus Status { get; set; } = TranslationStatus.Pending;
}
