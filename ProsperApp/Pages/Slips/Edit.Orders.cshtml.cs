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
            if (IsPartialRequest())
            {
                return Partial("_SlipOrders", this);
            }

            return Page();
        }

        if (!SlipOrderLineEditor.CanVoidStandardOrderLine(Detail!, orderLineId))
        {
            ModelState.AddModelError(string.Empty, "削除する注文を確認してください。");
            SetDefaultInputs();
            if (IsPartialRequest())
            {
                return Partial("_SlipOrders", this);
            }

            return Page();
        }

        var result = await _slipRepository.VoidOrderLineAsync(orderLineId, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "注文を削除できませんでした。");
            SetDefaultInputs();
            if (IsPartialRequest())
            {
                return Partial("_SlipOrders", this);
            }

            return Page();
        }

        SuccessMessage = "注文を削除しました。";
        ModelState.Clear();
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        if (IsPartialRequest())
        {
            return Partial("_SlipOrders", this);
        }

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
            if (IsPartialRequest())
            {
                return Partial("_SlipOrders", this);
            }

            return Page();
        }

        if (!CanAddOrders)
        {
            ModelState.AddModelError(string.Empty, "会計済みの伝票にオーダーは追加できません。");
            SetDefaultInputs();
            if (IsPartialRequest())
            {
                return Partial("_SlipOrders", this);
            }

            return Page();
        }

        ValidateOrderQueue();
        if (!ModelState.IsValid)
        {
            ShowOrderModal = true;
            SetDefaultInputs();
            if (IsPartialRequest())
            {
                return Partial("_SlipOrders", this);
            }

            return Page();
        }

        var result = await _orderRepository.AddOrderLinesAsync(SlipId!.Value, QueueLines, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "注文を登録できませんでした。");
            ShowOrderModal = true;
            SetDefaultInputs();
            if (IsPartialRequest())
            {
                return Partial("_SlipOrders", this);
            }

            return Page();
        }

        SuccessMessage = $"注文を登録しました。登録行数: {result.InsertedCount}";
        ModelState.Clear();
        QueueLines = [];
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        if (IsPartialRequest())
        {
            return Partial("_SlipOrders", this);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveOrderQuantitiesAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips))
        {
            return NotFound();
        }

        ClearCrossFormValidationState();
        await LoadAsync(cancellationToken);

        if (!EnsureSlipLoaded())
        {
            SetDefaultInputs();
            if (IsPartialRequest())
            {
                return Partial("_SlipOrders", this);
            }

            return Page();
        }

        PrepareOrderQuantities();
        if (!ModelState.IsValid)
        {
            SetDefaultInputs();
            if (IsPartialRequest())
            {
                return Partial("_SlipOrders", this);
            }

            return Page();
        }

        var result = await _slipRepository.SaveOrderLineQuantitiesAsync(SlipId!.Value, OrderQuantityLines, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "注文数量を保存できませんでした。");
            SetDefaultInputs();
            if (IsPartialRequest())
            {
                return Partial("_SlipOrders", this);
            }

            return Page();
        }

        SuccessMessage = "注文数量を保存しました。";
        ModelState.Clear();
        OrderQuantityLines = [];
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        if (IsPartialRequest())
        {
            return Partial("_SlipOrders", this);
        }

        return Page();
    }

    private void NormalizeQueue()
    {
        QueueLines = _orderQueueService.ReadPostedQueue(OrderQueueJson, QueueLines);
    }

    private void PrepareOrderQuantities()
    {
        var edit = SlipOrderLineEditor.PrepareQuantitySave(Detail!, OrderQuantityLines);
        OrderQuantityLines = edit.Lines;
        foreach (var error in edit.Errors)
        {
            ModelState.AddModelError(error.Key, error.Message);
        }
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

        foreach (var error in _orderQueueService.ValidateSlipOrderQueue(QueueLines, OrderItems, AttendanceCasts))
        {
            ModelState.AddModelError(nameof(QueueLines), error);
        }
    }

}
