using Microsoft.AspNetCore.Mvc;
using ProsperApp.Models;
using ProsperApp.Services;

namespace ProsperApp.Pages;

public partial class SlipEditModel
{
    public async Task<IActionResult> OnPostAddNominationsAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsEnabled(FeatureNames.Slips))
        {
            return NotFound();
        }

        NormalizeNominationInput();
        ClearCrossFormValidationState();
        await LoadAsync(cancellationToken);

        if (!EnsureSlipLoaded())
        {
            ShowAddNominationModal = true;
            if (IsPartialRequest())
            {
                return Partial("_SlipNominations", this);
            }

            return Page();
        }

        if (!IsOpenSlip)
        {
            ModelState.AddModelError(string.Empty, "会計済みの伝票に指名は追加できません。");
            ShowAddNominationModal = true;
            EnsureAddNominationRows();
            SetDefaultLeaveInput();
            if (IsPartialRequest())
            {
                return Partial("_SlipNominations", this);
            }

            return Page();
        }

        ValidateNominations();
        if (!ModelState.IsValid)
        {
            ShowAddNominationModal = true;
            EnsureAddNominationRows();
            SetDefaultLeaveInput();
            if (IsPartialRequest())
            {
                return Partial("_SlipNominations", this);
            }

            return Page();
        }

        var result = await _slipRepository.AddSlipNominationsAsync(SlipId!.Value, AddNominationsInput.CastNominations, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "指名を追加できませんでした。");
            ShowAddNominationModal = true;
            EnsureAddNominationRows();
            SetDefaultLeaveInput();
            if (IsPartialRequest())
            {
                return Partial("_SlipNominations", this);
            }

            return Page();
        }

        SuccessMessage = $"{result.AffectedCount}件の指名を追加しました。";
        ModelState.Clear();
        AddNominationsInput = new AddSlipNominationsInputModel();
        await LoadAsync(cancellationToken);
        SetDefaultInputs();
        if (IsPartialRequest())
        {
            return Partial("_SlipNominations", this);
        }

        return Page();
    }


    private void EnsureAddNominationRows()
    {
        if (AddNominationsInput.CastNominations.Count == 0)
        {
            AddNominationsInput.CastNominations.Add(new CastNominationInputModel());
        }
    }


    private void NormalizeNominationInput()
    {
        AddNominationsInput.CastNominations = AddNominationsInput.CastNominations
            .Select(x => new CastNominationInputModel
            {
                NominationKind = string.IsNullOrWhiteSpace(x.NominationKind) ? null : x.NominationKind.Trim(),
                CastId = x.CastId,
                CastName = string.IsNullOrWhiteSpace(x.CastName) ? null : x.CastName.Trim()
            })
            .Where(x => x.CastId is not null || !string.IsNullOrWhiteSpace(x.CastName) || !string.IsNullOrWhiteSpace(x.NominationKind))
            .ToList();
    }

    private void ValidateNominations()
    {
        if (AddNominationsInput.CastNominations.Count == 0)
        {
            ModelState.AddModelError("AddNominationsInput.CastNominations", "追加する指名を入力してください。");
            return;
        }

        if (AddNominationsInput.CastNominations.Count > 20)
        {
            ModelState.AddModelError("AddNominationsInput.CastNominations", "指名情報は20件まで追加できます。");
        }

        var allowedCastIds = AttendanceCasts.Select(x => x.CastId).ToHashSet();
        for (var i = 0; i < AddNominationsInput.CastNominations.Count; i++)
        {
            var nomination = AddNominationsInput.CastNominations[i];
            if (nomination.CastId is not null && string.IsNullOrWhiteSpace(nomination.CastName))
            {
                nomination.CastName = AttendanceCasts.FirstOrDefault(x => x.CastId == nomination.CastId.Value)?.SearchDisplayName;
            }

            if (string.IsNullOrWhiteSpace(nomination.NominationKind) || !AllowedNominationKinds.Contains(nomination.NominationKind))
            {
                ModelState.AddModelError($"AddNominationsInput.CastNominations[{i}].NominationKind", "指名区分を選択してください。");
            }

            if (nomination.CastId is null)
            {
                ModelState.AddModelError($"AddNominationsInput.CastNominations[{i}].CastName", "候補からキャストを選択してください。");
            }
            else if (!allowedCastIds.Contains(nomination.CastId.Value))
            {
                ModelState.AddModelError($"AddNominationsInput.CastNominations[{i}].CastName", "出勤キャストから選択してください。");
            }

            if (nomination.CastName is not null && nomination.CastName.Length > 160)
            {
                ModelState.AddModelError($"AddNominationsInput.CastNominations[{i}].CastName", "キャスト名は160文字以内で入力してください。");
            }
        }
    }
}
