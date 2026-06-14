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

        NormalizeCustomerInput();
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

        ComposeCustomerEnteredAt();
        ValidateAddCustomers();

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

        if (AddCustomersInput.CustomerLabels.Count is < 1 or > 20)
        {
            ModelState.AddModelError("AddCustomersInput.CustomerLabels", "客情報は1人から20人まで登録できます。");
        }

        if (AddCustomersInput.CustomerLabels.Any(x => x is not null && x.Length > 100))
        {
            ModelState.AddModelError("AddCustomersInput.CustomerLabels", "客名は1人100文字以内で入力してください。");
        }

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

        ComposeLeftAt();
        ValidateLeaveCustomer();
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
        UpdateCustomerInput.CustomerLabel = string.IsNullOrWhiteSpace(UpdateCustomerInput.CustomerLabel)
            ? null
            : UpdateCustomerInput.CustomerLabel.Trim();
        await LoadAsync(cancellationToken);

        if (!CanEditCustomerNames)
        {
            ModelState.AddModelError(string.Empty, "この伝票の客名は変更できません。");
        }

        ValidateUpdateCustomer();
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


    private void NormalizeCustomerInput()
    {
        AddCustomersInput.CustomerLabels = AddCustomersInput.CustomerLabels
            .Select(x => string.IsNullOrWhiteSpace(x) ? null : x.Trim())
            .ToList();

        AddCustomersInput.EnteredTime = string.IsNullOrWhiteSpace(AddCustomersInput.EnteredTime)
            ? null
            : AddCustomersInput.EnteredTime.Trim();

        EnsureAddCustomerRows();
    }

    private void ComposeCustomerEnteredAt()
    {
        if (Detail is null ||
            string.IsNullOrWhiteSpace(AddCustomersInput.EnteredTime) ||
            !TimeOnly.TryParse(AddCustomersInput.EnteredTime, out var enteredTime))
        {
            AddCustomersInput.EnteredAt = null;
            return;
        }

        AddCustomersInput.EnteredAt = _storeClock.ComposeBusinessDateTime(Detail.BusinessDate, enteredTime);
    }

    private void ValidateAddCustomers()
    {
        if (Detail is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(AddCustomersInput.EnteredTime) || !TimeOptions.Contains(AddCustomersInput.EnteredTime))
        {
            ModelState.AddModelError("AddCustomersInput.EnteredTime", "入店時刻は5分単位で選択してください。");
        }

        if (AddCustomersInput.EnteredAt is not null)
        {
            var openedAt = _storeClock.ToStoreDateTime(Detail.OpenedAt);
            if (AddCustomersInput.EnteredAt.Value < openedAt)
            {
                ModelState.AddModelError("AddCustomersInput.EnteredTime", "入店時刻は伝票の入店時刻以降で入力してください。");
            }

            if (AddCustomersInput.EnteredAt.Value > _storeClock.GetStoreNow().AddMinutes(5))
            {
                ModelState.AddModelError("AddCustomersInput.EnteredTime", "入店時刻に未来時刻は指定できません。");
            }
        }
    }


    private void ValidateLeaveCustomer()
    {
        if (Detail is null)
        {
            return;
        }

        var activeCustomerIds = Detail.Customers
            .Where(x => string.Equals(x.Status, "active", StringComparison.Ordinal))
            .Select(x => x.SlipCustomerId)
            .ToHashSet();

        if (LeaveCustomerInput.SlipCustomerId is null || !activeCustomerIds.Contains(LeaveCustomerInput.SlipCustomerId.Value))
        {
            ModelState.AddModelError("LeaveCustomerInput.SlipCustomerId", "退店する客を選択してください。");
        }

        if (string.IsNullOrWhiteSpace(LeaveCustomerInput.LeftTime) || !TimeOptions.Contains(LeaveCustomerInput.LeftTime))
        {
            ModelState.AddModelError("LeaveCustomerInput.LeftTime", "退店時刻は5分単位で選択してください。");
        }

        if (LeaveCustomerInput.LeftAt is not null)
        {
            var customer = Detail.Customers.FirstOrDefault(x => x.SlipCustomerId == LeaveCustomerInput.SlipCustomerId);
            if (customer is not null && LeaveCustomerInput.LeftAt.Value < _storeClock.ToStoreDateTime(customer.EnteredAt))
            {
                ModelState.AddModelError("LeaveCustomerInput.LeftTime", "退店時刻は入店時刻以降で入力してください。");
            }
        }
    }

    private void ValidateUpdateCustomer()
    {
        if (Detail is null)
        {
            return;
        }

        var editableCustomerIds = Detail.Customers
            .Where(x => !string.Equals(x.Status, "cancelled", StringComparison.Ordinal))
            .Select(x => x.SlipCustomerId)
            .ToHashSet();

        if (UpdateCustomerInput.SlipCustomerId is null || !editableCustomerIds.Contains(UpdateCustomerInput.SlipCustomerId.Value))
        {
            ModelState.AddModelError("UpdateCustomerInput.SlipCustomerId", "変更する客を確認してください。");
        }

        if (UpdateCustomerInput.CustomerLabel is not null && UpdateCustomerInput.CustomerLabel.Length > 100)
        {
            ModelState.AddModelError("UpdateCustomerInput.CustomerLabel", "客名は100文字以内で入力してください。");
        }
    }


    private void ComposeLeftAt()
    {
        if (Detail is null ||
            string.IsNullOrWhiteSpace(LeaveCustomerInput.LeftTime) ||
            !TimeOnly.TryParse(LeaveCustomerInput.LeftTime, out var leftTime))
        {
            LeaveCustomerInput.LeftAt = null;
            return;
        }

        LeaveCustomerInput.LeftAt = _storeClock.ComposeBusinessDateTime(Detail.BusinessDate, leftTime);
    }
}
