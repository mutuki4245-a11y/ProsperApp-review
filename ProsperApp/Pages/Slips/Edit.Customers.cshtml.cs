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
            if (IsPartialRequest())
            {
                return Partial("_SlipCustomers", this);
            }

            return Page();
        }

        if (!IsOpenSlip)
        {
            ModelState.AddModelError(string.Empty, "会計済みの伝票に退店登録はできません。");
            EnsureAddCustomerRows();
            if (IsPartialRequest())
            {
                return Partial("_SlipCustomers", this);
            }

            return Page();
        }

        PrepareLeaveCustomerInput();
        if (!ModelState.IsValid)
        {
            EnsureAddCustomerRows();
            if (IsPartialRequest())
            {
                return Partial("_SlipCustomers", this);
            }

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
            if (IsPartialRequest())
            {
                return Partial("_SlipCustomers", this);
            }

            return Page();
        }

        SuccessMessage = "客退店を登録しました。";
        ModelState.Clear();
        LeaveCustomerInput = new LeaveSlipCustomerInputModel();
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        if (IsPartialRequest())
        {
            return Partial("_SlipCustomers", this);
        }

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
        AddCustomersInput = SlipCustomerEditor.ApplyAddDefaults(AddCustomersInput, _storeClock);
    }


    private void SetDefaultLeaveInput()
    {
        LeaveCustomerInput = SlipCustomerEditor.ApplyLeaveDefaults(LeaveCustomerInput, _storeClock);
    }


    private void PrepareAddCustomerInput()
    {
        var edit = SlipCustomerEditor.PrepareAdd(AddCustomersInput, Detail!, TimeOptions, _storeClock);
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
