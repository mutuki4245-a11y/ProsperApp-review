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

    public async Task<IActionResult> OnPostSaveOrderQuantitiesAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips))
        {
            return NotFound();
        }

        ClearCrossFormValidationState();
        NormalizeOrderQuantities();
        await LoadAsync(cancellationToken);

        if (!EnsureSlipLoaded())
        {
            SetDefaultInputs();
            return Page();
        }

        ValidateOrderQuantities();
        if (!ModelState.IsValid)
        {
            SetDefaultInputs();
            return Page();
        }

        var result = await _slipRepository.SaveOrderLineQuantitiesAsync(SlipId!.Value, OrderQuantityLines, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "注文数量を保存できませんでした。");
            SetDefaultInputs();
            return Page();
        }

        SuccessMessage = "注文数量を保存しました。";
        ModelState.Clear();
        OrderQuantityLines = [];
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        return Page();
    }


    private void NormalizeQueue()
    {
        QueueLines = _orderQueueService.Normalize(QueueLines);
    }

    private void NormalizeOrderQuantities()
    {
        OrderQuantityLines = OrderQuantityLines
            .Where(x => x.OrderLineId > 0)
            .GroupBy(x => x.OrderLineId)
            .Select(x => new OrderLineQuantityInputModel
            {
                OrderLineId = x.Key,
                Quantity = x.Last().Quantity
            })
            .ToList();
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

    private void ValidateOrderQuantities()
    {
        if (Detail is null)
        {
            return;
        }

        if (!string.Equals(Detail.Status, "open", StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "営業中の伝票のみ注文数量を訂正できます。");
        }

        if (OrderQuantityLines.Count == 0)
        {
            ModelState.AddModelError(nameof(OrderQuantityLines), "訂正する注文数量がありません。");
            return;
        }

        var editableOrderIds = Detail.Orders
            .Where(x => string.Equals(x.Status, "active", StringComparison.Ordinal) && !x.IsKaraoke)
            .Select(x => x.OrderLineId)
            .ToHashSet();

        for (var i = 0; i < OrderQuantityLines.Count; i++)
        {
            var line = OrderQuantityLines[i];
            if (!editableOrderIds.Contains(line.OrderLineId))
            {
                ModelState.AddModelError($"OrderQuantityLines[{i}].OrderLineId", "訂正する注文を確認してください。");
            }

            if (line.Quantity < 0 || line.Quantity > 999 || line.Quantity != decimal.Truncate(line.Quantity))
            {
                ModelState.AddModelError($"OrderQuantityLines[{i}].Quantity", "注文数量を確認してください。");
            }
        }
    }
}
