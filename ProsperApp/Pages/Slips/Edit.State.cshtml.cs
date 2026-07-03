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
            ModelState.AddModelError(string.Empty, "伝票を取得できません。営業中画面から対象伝票を選択してください。");
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
        SetDefaultAdjustmentInput();
        SetDefaultKaraokeInput();
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

    private void SetDefaultAdjustmentInput()
    {
        if (AdjustmentsInput.Lines.Count > 0 || Detail is null)
        {
            return;
        }

        AdjustmentsInput.Lines = Detail.ChargeLines
            .Where(x => string.Equals(x.ChargeType, "adjustment", StringComparison.Ordinal) &&
                        string.Equals(x.Status, "active", StringComparison.Ordinal))
            .OrderBy(x => x.LineNo)
            .Select(x => new SlipAdjustmentInputModel
            {
                LineName = x.LineName,
                Amount = x.Amount
            })
            .ToList();

        if (AdjustmentsInput.Lines.Count == 0)
        {
            AdjustmentsInput.Lines.Add(new SlipAdjustmentInputModel());
        }
    }

    private void SetDefaultKaraokeInput()
    {
        if (KaraokeLines.Count > 0 || Detail is null)
        {
            return;
        }

        var quantity = Detail.ChargeLines
            .Where(x => string.Equals(x.ChargeType, "karaoke", StringComparison.Ordinal) &&
                        string.Equals(x.Status, "active", StringComparison.Ordinal))
            .Sum(x => x.Quantity);

        KaraokeLines =
        [
            new KaraokeQuantityInputModel
            {
                SlipId = Detail.SlipId,
                Quantity = quantity
            }
        ];
    }
}
