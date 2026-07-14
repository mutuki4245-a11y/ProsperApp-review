using Microsoft.AspNetCore.Mvc;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public partial class SlipEditModel
{
    public async Task<IActionResult> OnPostAddCustomersAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips))
        {
            return NotFound();
        }

        ClearCrossFormValidationState();
        await LoadAsync(cancellationToken);

        if (!EnsureSlipLoaded())
        {
            ShowAddCustomerModal = true;
            if (IsPartialRequest())
            {
                return Partial("_SlipCustomers", this);
            }

            return Page();
        }

        if (!IsOpenSlip)
        {
            ModelState.AddModelError(string.Empty, "会計済みの伝票に客は追加できません。");
            ShowAddCustomerModal = true;
            SetDefaultInputs();
            if (IsPartialRequest())
            {
                return Partial("_SlipCustomers", this);
            }

            return Page();
        }

        PrepareAddCustomerInput();

        if (!ModelState.IsValid)
        {
            ShowAddCustomerModal = true;
            SetDefaultLeaveInput();
            if (IsPartialRequest())
            {
                return Partial("_SlipCustomers", this);
            }

            return Page();
        }

        var result = await _slipRepository.AddSlipCustomersAsync(
            SlipId!.Value,
            AddCustomersInput.CustomerLabels,
            AddCustomersInput.EnteredAt!.Value,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "客を追加できませんでした。");
            ShowAddCustomerModal = true;
            SetDefaultLeaveInput();
            if (IsPartialRequest())
            {
                return Partial("_SlipCustomers", this);
            }

            return Page();
        }

        SuccessMessage = $"{result.AffectedCount}人の客を追加しました。";
        ModelState.Clear();
        AddCustomersInput = new AddSlipCustomersInputModel();
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        if (IsPartialRequest())
        {
            return Partial("_SlipCustomers", this);
        }

        return Page();
    }


    public async Task<IActionResult> OnPostLeaveCustomerAsync(CancellationToken cancellationToken)
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

        if (!IsOpenSlip)
        {
            ModelState.AddModelError(string.Empty, "会計済みの伝票に退店登録はできません。");
            EnsureAddCustomerRows();
            return Page();
        }

        PrepareLeaveCustomerInput();
        if (!ModelState.IsValid)
        {
            EnsureAddCustomerRows();
            return Page();
        }

        var result = await _slipRepository.LeaveSlipCustomerAsync(
            LeaveCustomerInput.SlipCustomerId!.Value,
            LeaveCustomerInput.LeftAt!.Value,
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "客退店を登録できませんでした。");
            EnsureAddCustomerRows();
            return Page();
        }

        SuccessMessage = "客退店を登録しました。";
        ModelState.Clear();
        LeaveCustomerInput = new LeaveSlipCustomerInputModel();
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateCustomerAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips))
        {
            return NotFound();
        }

        ClearCrossFormValidationState();
        await LoadAsync(cancellationToken);

        if (!CanEditCustomerNames || Detail is null)
        {
            ModelState.AddModelError(string.Empty, "この伝票の客名は変更できません。");
        }
        else
        {
            PrepareUpdateCustomerInput();
        }
        if (!ModelState.IsValid)
        {
            SetDefaultInputs();
            if (IsPartialRequest())
            {
                return Partial("_SlipCustomers", this);
            }

            return Page();
        }

        var result = await _slipRepository.UpdateSlipCustomerLabelAsync(
            UpdateCustomerInput.SlipCustomerId!.Value,
            UpdateCustomerInput.CustomerLabel,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "客名を変更できませんでした。");
            SetDefaultInputs();
            if (IsPartialRequest())
            {
                return Partial("_SlipCustomers", this);
            }

            return Page();
        }

        SuccessMessage = "客名を変更しました。";
        ModelState.Clear();
        UpdateCustomerInput = new UpdateSlipCustomerInputModel();
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        if (IsPartialRequest())
        {
            return Partial("_SlipCustomers", this);
        }

        return Page();
    }


    private void EnsureAddCustomerRows()
    {
        if (AddCustomersInput.CustomerLabels.Count == 0)
        {
            AddCustomersInput.CustomerLabels.Add(null);
        }

        AddCustomersInput.EnteredTime ??= _storeClock.FloorToMinuteStep(_storeClock.GetStoreNow(), 5).ToString("HH:mm");
    }


    private void SetDefaultLeaveInput()
    {
        LeaveCustomerInput.LeftTime ??= _storeClock.FloorToMinuteStep(_storeClock.GetStoreNow(), 5).ToString("HH:mm");
    }


    private void PrepareAddCustomerInput()
    {
        var defaultEnteredTime = _storeClock.FloorToMinuteStep(_storeClock.GetStoreNow(), 5).ToString("HH:mm");
        var edit = SlipCustomerEditor.PrepareAdd(AddCustomersInput, Detail!, TimeOptions, _storeClock, defaultEnteredTime);
        AddCustomersInput = edit.Input;
        foreach (var error in edit.Errors)
        {
            ModelState.AddModelError(error.Key, error.Message);
        }
    }

    private void PrepareLeaveCustomerInput()
    {
        var edit = SlipCustomerEditor.PrepareLeave(LeaveCustomerInput, Detail!, TimeOptions, _storeClock);
        LeaveCustomerInput = edit.Input;
        foreach (var error in edit.Errors)
        {
            ModelState.AddModelError(error.Key, error.Message);
        }
    }

    private void PrepareUpdateCustomerInput()
    {
        var edit = SlipCustomerEditor.PrepareUpdate(UpdateCustomerInput, Detail!);
        UpdateCustomerInput = edit.Input;
        foreach (var error in edit.Errors)
        {
            ModelState.AddModelError(error.Key, error.Message);
        }
    }
}
