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
        var attendanceCastsTask = GetAttendanceCastsForCurrentBusinessDayAsync(currentBusinessDayTask, cancellationToken);

        await Task.WhenAll(detailTask, orderItemsTask, nominationOptionsTask, attendanceCastsTask);

        CurrentBusinessDay = await currentBusinessDayTask;
        Detail = await detailTask;
        OrderItems = CurrentBusinessDay is null
            ? []
            : await orderItemsTask;
        AttendanceCasts = await attendanceCastsTask;
        NominationOptions = (await nominationOptionsTask)
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToList();
        TimeOptions = _storeClock.BuildTimeOptions(5);
    }

    private async Task<IReadOnlyList<StoreOrderAttendanceCastOption>> GetAttendanceCastsForCurrentBusinessDayAsync(
        Task<StoreBusinessDay?> currentBusinessDayTask,
        CancellationToken cancellationToken)
    {
        var currentBusinessDay = await currentBusinessDayTask;
        return currentBusinessDay is null
            ? []
            : await _orderRepository.GetAttendanceCastsAsync(currentBusinessDay.BusinessDayId, cancellationToken);
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
        return _storeClock.FormatBusinessTime(value);
    }

    public string FormatStoreTime(DateTimeOffset? value, string fallback = "-")
    {
        return _storeClock.FormatBusinessTime(value, fallback);
    }

    public string FormatBusinessTimeOption(string value)
    {
        return TimeOnly.TryParse(value, out var time)
            ? _storeClock.FormatBusinessTime(time)
            : value;
    }
}
