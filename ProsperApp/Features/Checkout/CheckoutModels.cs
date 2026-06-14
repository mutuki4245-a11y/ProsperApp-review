using System.ComponentModel.DataAnnotations;

namespace ProsperApp.Models;

public class CheckoutPaymentInputModel
{
    public string MethodCode { get; set; } = string.Empty;

    public string MethodName { get; set; } = string.Empty;

    public bool IsSelected { get; set; }

    [Range(0, 99999999, ErrorMessage = "決済金額を確認してください。")]
    public decimal Amount { get; set; }
}

public class CheckoutInputModel
{
    [Display(Name = "退店時刻")]
    [Required(ErrorMessage = "退店時刻を選択してください。")]
    public string? ClosedTime { get; set; }

    public DateTime? ClosedAt { get; set; }

    public List<CheckoutPaymentInputModel> Payments { get; set; } = [];

    [Display(Name = "受取額")]
    [Range(0, 99999999, ErrorMessage = "受取額を確認してください。")]
    public decimal? ReceivedAmount { get; set; }
}

public class CheckoutTotals
{
    public decimal SubtotalAmount { get; set; }
    public decimal ServiceTaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
}

public class ConfirmCheckoutResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public long? CheckoutId { get; init; }
    public decimal ChangeAmount { get; init; }

    public static ConfirmCheckoutResult Success(long checkoutId, decimal changeAmount)
    {
        return new ConfirmCheckoutResult { Succeeded = true, CheckoutId = checkoutId, ChangeAmount = changeAmount };
    }

    public static ConfirmCheckoutResult Failed(string message)
    {
        return new ConfirmCheckoutResult { Succeeded = false, ErrorMessage = message };
    }
}
