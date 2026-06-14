using Microsoft.AspNetCore.Mvc;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public partial class SlipEditModel
{
    public async Task<IActionResult> OnPostVoidOrderAsync(long orderLineId, CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips))
        {
            return NotFound();
        }

        await LoadAsync(cancellationToken);

        if (!EnsureSlipLoaded())
        {
            return Page();
        }

        if (Detail!.Orders.All(x => x.OrderLineId != orderLineId || !string.Equals(x.Status, "active", StringComparison.Ordinal)))
        {
            ModelState.AddModelError(string.Empty, "削除する注文を確認してください。");
            SetDefaultInputs();
            return Page();
        }

        var result = await _slipRepository.VoidOrderLineAsync(orderLineId, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "注文を削除できませんでした。");
            SetDefaultInputs();
            return Page();
        }

        SuccessMessage = "注文を削除しました。";
        ModelState.Clear();
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        return Page();
    }

    public async Task<IActionResult> OnPostAddOrdersAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips) || !_featureGate.IsEnabled(FeatureNames.Orders))
        {
            return NotFound();
        }

        NormalizeQueue();
        ClearCrossFormValidationState();
        await LoadAsync(cancellationToken);

        if (!EnsureSlipLoaded())
        {
            ShowOrderModal = true;
            SetDefaultInputs();
            return Page();
        }

        if (!CanAddOrders)
        {
            ModelState.AddModelError(string.Empty, "会計済みの伝票にオーダーは追加できません。");
            SetDefaultInputs();
            return Page();
        }

        ValidateOrderQueue();
        if (!ModelState.IsValid)
        {
            ShowOrderModal = true;
            SetDefaultInputs();
            return Page();
        }

        var result = await _orderRepository.AddOrderLinesAsync(SlipId!.Value, QueueLines, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "注文を登録できませんでした。");
            ShowOrderModal = true;
            SetDefaultInputs();
            return Page();
        }

        SuccessMessage = $"注文を登録しました。登録行数: {result.InsertedCount}";
        ModelState.Clear();
        QueueLines = [];
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        return Page();
    }


    private void NormalizeQueue()
    {
        QueueLines = _orderQueueService.Normalize(QueueLines);
    }

    private void ValidateOrderQueue()
    {
        if (Detail is null)
        {
            return;
        }

        if (!string.Equals(Detail.Status, "open", StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "営業中の伝票のみオーダーを追加できます。");
        }

        foreach (var error in _orderQueueService.Validate(
                     QueueLines,
                     OrderItems,
                     AttendanceCasts,
                     requireAttendingCastForBackTarget: true))
        {
            ModelState.AddModelError(nameof(QueueLines), error);
        }
    }
}
