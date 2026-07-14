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

        PrepareNominationInput();
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

        if (result.AffectedCount <= 0)
        {
            ModelState.AddModelError(string.Empty, "指名を追加できませんでした。キャストを選択してください。");
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
            AddNominationsInput.CastNominations.Add(new CastNominationInputModel
            {
                NominationKind = SlipNominationEditor.GetDefaultNominationKind(NominationOptions)
            });
        }
    }


    private void PrepareNominationInput()
    {
        var edit = SlipNominationEditor.PrepareAdd(
            AddNominationsInput.CastNominations,
            NominationOptions,
            AttendanceCasts);
        AddNominationsInput.CastNominations = edit.Nominations;
        foreach (var error in edit.Errors)
        {
            ModelState.AddModelError(error.Key, error.Message);
        }
    }
}
