using Microsoft.AspNetCore.Mvc;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public partial class SlipEditModel
{
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var currentBusinessDayTask = _businessDayRepository.GetCurrentAsync(cancellationToken);
        var detailTask = SlipId is null
            ? Task.FromResult<SlipDetail?>(null)
            : _slipRepository.GetSlipDetailAsync(SlipId.Value, cancellationToken);
        var orderItemsTask = _featureGate.IsEnabled(FeatureNames.Orders)
            ? _orderRepository.GetItemsAsync(cancellationToken)
            : Task.FromResult<IReadOnlyList<StoreOrderItemOption>>([]);
        var nominationOptionsTask = _nominationBackRepository.GetSettingsAsync(cancellationToken);

        await Task.WhenAll(currentBusinessDayTask, detailTask, orderItemsTask, nominationOptionsTask);

        CurrentBusinessDay = await currentBusinessDayTask;
        Detail = await detailTask;
        OrderItems = CurrentBusinessDay is null
            ? []
            : await orderItemsTask;
        AttendanceCasts = CurrentBusinessDay is null
            ? []
            : await _orderRepository.GetAttendanceCastsAsync(CurrentBusinessDay.BusinessDayId, cancellationToken);
        NominationOptions = (await nominationOptionsTask)
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToList();
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

    private bool EnsureCheckedOutSlipLoaded()
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

        if (!string.Equals(Detail.Status, "checked_out", StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "会計済みの伝票のみ会計取消できます。");
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
}
