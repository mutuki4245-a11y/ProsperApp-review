using Microsoft.AspNetCore.Mvc;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public partial class SlipEditModel
{
    public async Task<IActionResult> OnPostStartCheckoutAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips) || !_featureGate.IsEnabled(FeatureNames.Checkout))
        {
            return NotFound();
        }

        ClearCrossFormValidationState();
        await LoadAsync(cancellationToken);
        NormalizeCheckoutInput();
        ValidateCheckoutInput(requireReceivedAmount: false);

        if (!ModelState.IsValid)
        {
            ShowCheckoutModal = true;
            SetDefaultInputs();
            return Page();
        }

        if (HasSelectedCashPayment())
        {
            ShowCheckoutModal = true;
            ShowCashReceivedStep = true;
            SetDefaultInputs();
            return Page();
        }

        var result = await _checkoutRepository.ConfirmCheckoutAsync(SlipId!.Value, CheckoutInput, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "会計を確定できませんでした。");
            ShowCheckoutModal = true;
            SetDefaultInputs();
            return Page();
        }

        TempData["SuccessMessage"] = "会計を確定しました。";
        ModelState.Clear();
        return RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostConfirmCashAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips) || !_featureGate.IsEnabled(FeatureNames.Checkout))
        {
            return NotFound();
        }

        ClearCrossFormValidationState();
        await LoadAsync(cancellationToken);
        NormalizeCheckoutInput();
        ValidateCheckoutInput(requireReceivedAmount: true);

        if (!ModelState.IsValid)
        {
            ShowCheckoutModal = true;
            ShowCashReceivedStep = true;
            SetDefaultInputs();
            return Page();
        }

        var result = await _checkoutRepository.ConfirmCheckoutAsync(SlipId!.Value, CheckoutInput, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "会計を確定できませんでした。");
            ShowCheckoutModal = true;
            ShowCashReceivedStep = true;
            SetDefaultInputs();
            return Page();
        }

        TempData["SuccessMessage"] = $"会計を確定しました。お釣り: {result.ChangeAmount:N0}円";
        ModelState.Clear();
        return RedirectToPage("/Index");
    }


    private void SetDefaultCheckoutInput()
    {
        EnsurePaymentRows();
        CheckoutInput.ClosedTime ??= _storeClock.FloorToMinuteStep(_storeClock.GetStoreNow(), 5).ToString("HH:mm");
    }

    private void EnsurePaymentRows()
    {
        var current = CheckoutInput.Payments
            .Where(x => !string.IsNullOrWhiteSpace(x.MethodCode))
            .GroupBy(x => x.MethodCode.Trim().ToLowerInvariant())
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        CheckoutInput.Payments = PaymentTemplates
            .Select(template =>
            {
                if (!current.TryGetValue(template.MethodCode, out var existing))
                {
                    return new CheckoutPaymentInputModel
                    {
                        MethodCode = template.MethodCode,
                        MethodName = template.MethodName
                    };
                }

                existing.MethodCode = template.MethodCode;
                existing.MethodName = template.MethodName;
                return existing;
            })
            .ToList();
    }


    private void NormalizeCheckoutInput()
    {
        EnsurePaymentRows();
        CheckoutInput.Payments = CheckoutInput.Payments
            .Select(x => new CheckoutPaymentInputModel
            {
                MethodCode = x.MethodCode.Trim().ToLowerInvariant(),
                MethodName = x.MethodName.Trim(),
                IsSelected = x.IsSelected,
                Amount = x.Amount
            })
            .ToList();

        ComposeCheckoutClosedAt();
    }

    private void ComposeCheckoutClosedAt()
    {
        if (Detail is null ||
            string.IsNullOrWhiteSpace(CheckoutInput.ClosedTime) ||
            !TimeOnly.TryParse(CheckoutInput.ClosedTime, out var closedTime))
        {
            CheckoutInput.ClosedAt = null;
            return;
        }

        CheckoutInput.ClosedAt = _storeClock.ComposeBusinessDateTime(Detail.BusinessDate, closedTime);
    }

    private void ValidateCheckoutInput(bool requireReceivedAmount)
    {
        if (!EnsureSlipLoaded())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CheckoutInput.ClosedTime) || !TimeOptions.Contains(CheckoutInput.ClosedTime))
        {
            ModelState.AddModelError("CheckoutInput.ClosedTime", "退店時刻は5分単位で選択してください。");
        }

        if (CheckoutInput.ClosedAt is null || CheckoutInput.ClosedAt.Value < _storeClock.ToStoreDateTime(Detail!.OpenedAt))
        {
            ModelState.AddModelError("CheckoutInput.ClosedTime", "退店時刻は入店時刻以降で入力してください。");
        }

        var selectedPayments = CheckoutInput.Payments.Where(x => x.IsSelected).ToList();
        if (selectedPayments.Count == 0)
        {
            ModelState.AddModelError("CheckoutInput.Payments", "決済方法を選択してください。");
            return;
        }

        foreach (var payment in selectedPayments)
        {
            if (payment.Amount <= 0)
            {
                ModelState.AddModelError("CheckoutInput.Payments", "選択した決済方法の金額を入力してください。");
                break;
            }
        }

        var selectedTotal = selectedPayments.Sum(x => x.Amount);
        if (selectedTotal != CheckoutTotals.TotalAmount)
        {
            ModelState.AddModelError("CheckoutInput.Payments", "決済金額の合計が合計額と一致していません。");
        }

        var cashAmount = selectedPayments
            .Where(x => x.MethodCode == "cash")
            .Sum(x => x.Amount);

        if (requireReceivedAmount)
        {
            if (cashAmount <= 0)
            {
                ModelState.AddModelError("CheckoutInput.Payments", "現金決済が選択されていません。");
            }

            if (CheckoutInput.ReceivedAmount is null)
            {
                ModelState.AddModelError("CheckoutInput.ReceivedAmount", "受取額を入力してください。");
            }
            else if (CheckoutInput.ReceivedAmount.Value < cashAmount)
            {
                ModelState.AddModelError("CheckoutInput.ReceivedAmount", "受取額が現金決済額を下回っています。");
            }
        }
    }

    private bool HasSelectedCashPayment()
    {
        return CheckoutInput.Payments.Any(x => x.IsSelected && x.MethodCode == "cash");
    }

    private CheckoutTotals CalculateCheckoutTotals()
    {
        var subtotal = Detail?.Orders
            .Where(x => string.Equals(x.Status, "active", StringComparison.Ordinal))
            .Sum(x => x.Amount) ?? 0;
        var serviceTax = Math.Round(subtotal * 0.20m, 0, MidpointRounding.AwayFromZero);

        return new CheckoutTotals
        {
            SubtotalAmount = subtotal,
            ServiceTaxAmount = serviceTax,
            TotalAmount = subtotal + serviceTax
        };
    }
}
