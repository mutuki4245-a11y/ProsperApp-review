using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public partial class SlipEditModel
{
    private static readonly JsonSerializerOptions ReceiptPrintJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IActionResult> OnPostStartCheckoutAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips) || !_featureGate.IsEnabled(FeatureNames.Checkout))
        {
            return NotFound();
        }

        ClearCrossFormValidationState();
        await LoadAsync(cancellationToken);
        if (!EnsureSlipLoaded())
        {
            ShowCheckoutModal = true;
            SetDefaultInputs();
            return Page();
        }

        var edit = PrepareCheckoutInput(requireReceivedAmount: false);

        if (!ModelState.IsValid)
        {
            ShowCheckoutModal = true;
            SetDefaultInputs();
            return Page();
        }

        if (edit.HasSelectedCashPayment)
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
            if (result.RequiresReload)
            {
                await LoadAsync(cancellationToken);
            }

            ShowCheckoutModal = true;
            SetDefaultInputs();
            return Page();
        }

        QueueReceiptPrint(result);
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
        if (!EnsureSlipLoaded())
        {
            ShowCheckoutModal = true;
            ShowCashReceivedStep = true;
            SetDefaultInputs();
            return Page();
        }

        PrepareCheckoutInput(requireReceivedAmount: true);

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
            if (result.RequiresReload)
            {
                await LoadAsync(cancellationToken);
            }

            ShowCheckoutModal = true;
            ShowCashReceivedStep = true;
            SetDefaultInputs();
            return Page();
        }

        QueueReceiptPrint(result);
        TempData["SuccessMessage"] = $"会計を確定しました。お釣り: {result.ChangeAmount:N0}円";
        ModelState.Clear();
        return RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostCancelCheckoutAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips) || !_featureGate.IsEnabled(FeatureNames.Checkout))
        {
            return NotFound();
        }

        ClearCrossFormValidationState();
        await LoadAsync(cancellationToken);
        if (!EnsureCheckedOutSlipLoaded())
        {
            SetDefaultInputs();
            return Page();
        }

        var result = await _checkoutRepository.CancelCheckoutAsync(SlipId!.Value, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "会計取消を実行できませんでした。");
            SetDefaultInputs();
            return Page();
        }

        SuccessMessage = "会計を取消しました。";
        ModelState.Clear();
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        return Page();
    }


    private void SetDefaultCheckoutInput()
    {
        var defaultClosedTime = _storeClock.FloorToMinuteStep(_storeClock.GetStoreNow(), 5).ToString("HH:mm");
        CheckoutInput = CheckoutInputEditor.ApplyDefaults(CheckoutInput, defaultClosedTime);
    }


    private CheckoutInputEdit PrepareCheckoutInput(bool requireReceivedAmount)
    {
        var edit = CheckoutInputEditor.PrepareConfirm(
            CheckoutInput,
            Detail!,
            CheckoutTotals,
            TimeOptions,
            _storeClock,
            requireReceivedAmount);
        CheckoutInput = edit.Input;
        foreach (var error in edit.Errors)
        {
            ModelState.AddModelError(error.Key, error.Message);
        }

        return edit;
    }

    private CheckoutTotals CalculateCheckoutTotals()
    {
        return CheckoutDocumentBuilder.CalculateTotals(Detail);
    }

    public string BuildCheckoutSnapshotJson()
    {
        if (Detail is null)
        {
            return "{}";
        }

        return CheckoutDocumentBuilder.BuildConfirmedSnapshotJson(Detail, CheckoutTotals);
    }

    private void QueueReceiptPrint(ConfirmCheckoutResult result)
    {
        if (Detail is null || result.CheckoutId is null || CheckoutInput.ClosedAt is null)
        {
            return;
        }

        var settings = _localSettingsProvider.GetCurrent();
        var closedAt = _storeClock.ToStoreDateTimeOffset(CheckoutInput.ClosedAt.Value);
        var request = CheckoutDocumentBuilder.BuildReceiptPrintRequest(
            Detail,
            CheckoutTotals,
            CheckoutInput,
            result,
            settings.StoreName,
            closedAt);
        if (_receiptPrinterOptions.Enabled)
        {
            TempData[ReceiptPrintTempDataKeys.PendingCheckoutReceipt] =
                JsonSerializer.Serialize(request, ReceiptPrintJsonOptions);
        }
    }
}
