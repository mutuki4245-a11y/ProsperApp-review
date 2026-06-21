using System.ComponentModel.DataAnnotations;

namespace ProsperApp.Models;

public class DrinkDeliveryInputModel
{
    public long? BusinessDayId { get; set; }

    [Display(Name = "納品額")]
    [Range(0, 999999999999, ErrorMessage = "納品額は0円以上で入力してください。")]
    public decimal DrinkDeliveryAmount { get; set; }
}

public class BusinessDayAmountSaveResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public decimal Amount { get; init; }

    public static BusinessDayAmountSaveResult Success(decimal amount)
    {
        return new BusinessDayAmountSaveResult { Succeeded = true, Amount = amount };
    }

    public static BusinessDayAmountSaveResult Failed(string message)
    {
        return new BusinessDayAmountSaveResult { Succeeded = false, ErrorMessage = message };
    }
}
