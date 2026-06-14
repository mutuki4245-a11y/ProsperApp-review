using Microsoft.AspNetCore.Mvc;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public partial class SlipEditModel
{
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CurrentBusinessDay = await _businessDayRepository.GetCurrentAsync(cancellationToken);
        Detail = SlipId is null ? null : await _slipRepository.GetSlipDetailAsync(SlipId.Value, cancellationToken);
        AttendanceCasts = CurrentBusinessDay is null
            ? []
            : await _orderRepository.GetAttendanceCastsAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        OrderItems = CurrentBusinessDay is null || !_featureGate.IsEnabled(FeatureNames.Orders)
            ? []
            : await _orderRepository.GetItemsAsync(cancellationToken);
        TimeOptions = _storeClock.BuildTimeOptions(5);
        CheckoutTotals = CalculateCheckoutTotals();
    }

    private bool EnsureSlipLoaded()
    {
        if (SlipId is null)
        {
            ModelState.AddModelError(string.Empty, "伝票を選択してください。");
            return false;
        }

        if (Detail is null)
        {
            ModelState.AddModelError(string.Empty, "伝票を取得できません。ホームから対象伝票を選択してください。");
            return false;
        }

        if (!string.Equals(Detail.Status, "open", StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "営業中の伝票のみ編集できます。");
            return false;
        }

        return true;
    }

    private void SetDefaultInputs()
    {
        EnsureAddCustomerRows();
        EnsureAddNominationRows();
        SetDefaultLeaveInput();
        SetDefaultCheckoutInput();
    }

    private void ClearCrossFormValidationState()
    {
        // This page has several independent forms. A handler validates only its own form fields below.
        ModelState.Clear();
    }

    private bool IsPartialRequest()
    {
        return string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.Ordinal);
    }


    public string FormatStoreTime(DateTimeOffset value)
    {
        return _storeClock.FormatStoreTime(value);
    }

    public string FormatStoreTime(DateTimeOffset? value, string fallback = "-")
    {
        return _storeClock.FormatStoreTime(value, fallback);
    }
}
