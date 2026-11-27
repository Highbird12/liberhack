using System.ComponentModel.DataAnnotations;

namespace BookLibrary.Models;

// Form model for adding and editing books in the admin area.
// Kept separate from Book so a form post cannot set fields like Id or Translations.
public class BookForm
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Title { get; set; } = "";

    [Required, StringLength(200)]
    public string Author { get; set; } = "";

    [Required, StringLength(100)]
    public string Nationality { get; set; } = "";

    [StringLength(4000)]
    public string? Summary { get; set; }

    [Display(Name = "Cover image (.png, .jpg, .webp)")]
    public IFormFile? Cover { get; set; }

    [Display(Name = "English PDF")]
    public IFormFile? Pdf { get; set; }

    [Display(Name = "Remove current cover")]
    public bool RemoveCover { get; set; }

    [Display(Name = "Remove current PDF")]
    public bool RemovePdf { get; set; }

    // Shown on the edit page only; never read back from the post.
    public string? CurrentCoverUrl { get; set; }
    public string? CurrentPdf { get; set; }
}
