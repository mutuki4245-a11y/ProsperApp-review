using System.ComponentModel.DataAnnotations;

namespace ProsperApp.Models;

public class ReceiptInputModel
{
    [Display(Name = "Amount")]
    [Required(ErrorMessage = "Amount is required.")]
    [Range(typeof(decimal), "0.01", "999999999.99", ErrorMessage = "Amount must be greater than zero.")]
    public decimal? Amount { get; set; }

    [Display(Name = "Account Subject")]
    [Required(ErrorMessage = "Account subject is required.")]
    [StringLength(100, ErrorMessage = "Account subject must be 100 characters or less.")]
    public string AccountSubject { get; set; } = string.Empty;

    [Display(Name = "Description")]
    [Required(ErrorMessage = "Description is required.")]
    [StringLength(500, ErrorMessage = "Description must be 500 characters or less.")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Group Code")]
    [StringLength(32, ErrorMessage = "Group code must be 32 characters or less.")]
    public string? GroupCode { get; set; }
}
