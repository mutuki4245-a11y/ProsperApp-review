using ProsperApp.Services;

namespace ProsperApp.Tests;

public class CreateSlipEditorTests
{
    private static readonly DateOnly BusinessDate = new(2026, 7, 30);

    [Fact]
    public void Prepare_AcceptsValidSlipAndNormalizesEditableFields()
    {
        var edit = CreateSlipEditor.Prepare(
            new CreateSlipInputModel
            {
                TableId = 10,
                OpenedTime = " 20:00 ",
                CustomerLabels = ["  山田様  ", "   "],
                CastNominations =
                [
                    new CastNominationInputModel
                    {
                        NominationKind = " regular ",
                        NominationPrice = 2000,
                        CastId = 20
                    }
                ],
                Memo = "  来店メモ  "
            },
            CreateValidContext(),
            CreateClock());

        Assert.Empty(edit.Errors);
        Assert.Equal(BusinessDate, edit.Input.BusinessDate);
        Assert.Equal(new DateTime(2026, 7, 30, 20, 0, 0), edit.Input.OpenedAt);
        Assert.Equal(["山田様", null], edit.Input.CustomerLabels);
        Assert.Equal("来店メモ", edit.Input.Memo);

        var nomination = Assert.Single(edit.Input.CastNominations);
        Assert.Equal("regular", nomination.NominationKind);
        Assert.Equal("佐藤", nomination.CastName);
    }

    [Fact]
    public void Prepare_ReportsMissingOperationalPrerequisites()
    {
        var context = CreateValidContext() with
        {
            StoreContext = null,
            Tables = [],
            AttendanceCasts = [],
            IsPreviousBusinessDayOpen = true
        };

        var edit = CreateSlipEditor.Prepare(
            new CreateSlipInputModel
            {
                OpenedTime = "20:00",
                CustomerLabels = [null]
            },
            context,
            CreateClock());

        Assert.Contains(edit.Errors, error =>
            error.Message == "店舗設定を取得できません。Supabase設定とStoreDepartmentIdを確認してください。");
        Assert.Contains(edit.Errors, error =>
            error.Message == "前回営業日 2026-07-30 の締め作業が未完了です。締め作業を完了してから新しい営業入力を開始してください。");
        Assert.Contains(edit.Errors, error =>
            error.Message == "卓番が未登録です。店舗設定の「卓番情報」から登録してください。");
        Assert.Contains(edit.Errors, error =>
            error.Message == CreateSlipEditor.AttendanceRequiredMessage);
    }

    [Fact]
    public void Prepare_RejectsUnavailableTableAndInvalidNominations()
    {
        var edit = CreateSlipEditor.Prepare(
            new CreateSlipInputModel
            {
                TableId = 999,
                OpenedTime = "20:00",
                CustomerLabels = [null],
                CastNominations =
                [
                    new CastNominationInputModel
                    {
                        NominationKind = "unknown",
                        NominationPrice = 1500,
                        CastId = 99,
                        CastName = "未出勤キャスト"
                    },
                    new CastNominationInputModel
                    {
                        NominationKind = "regular",
                        NominationPrice = 2000,
                        CastId = 20
                    },
                    new CastNominationInputModel
                    {
                        NominationKind = "regular",
                        NominationPrice = 2000,
                        CastId = 20
                    }
                ]
            },
            CreateValidContext(),
            CreateClock());

        Assert.Contains(edit.Errors, error =>
            error.Key == "CreateSlipInput.TableId" &&
            error.Message == "この店舗で利用できない卓番です。");
        Assert.Contains(edit.Errors, error =>
            error.Key == "CreateSlipInput.CastNominations[0].NominationKind" &&
            error.Message == "指名区分を選択してください。");
        Assert.Contains(edit.Errors, error =>
            error.Key == "CreateSlipInput.CastNominations[0].NominationPrice" &&
            error.Message == "指名料金を選択してください。");
        Assert.Contains(edit.Errors, error =>
            error.Key == "CreateSlipInput.CastNominations[0].CastName" &&
            error.Message == "出勤キャストから選択してください。");
        Assert.Equal(
            2,
            edit.Errors.Count(error =>
                error.Message == "同じキャストは1枚の伝票に重複して指名登録できません。"));
    }

    [Fact]
    public void Prepare_RejectsEmptySubmittedCustomerList()
    {
        var edit = CreateSlipEditor.Prepare(
            new CreateSlipInputModel
            {
                TableId = 10,
                OpenedTime = "20:00",
                CustomerLabels = []
            },
            CreateValidContext(),
            CreateClock());

        Assert.Contains(edit.Errors, error =>
            error.Key == "CreateSlipInput.CustomerLabels" &&
            error.Message == "お客様情報は1人から20人まで登録できます。");
        Assert.Single(edit.Input.CustomerLabels);
    }

    [Fact]
    public void Prepare_RejectsOpenedTimeMoreThanFiveMinutesInTheFuture()
    {
        var edit = CreateSlipEditor.Prepare(
            new CreateSlipInputModel
            {
                TableId = 10,
                OpenedTime = "21:10",
                CustomerLabels = [null]
            },
            CreateValidContext(timeOptions: ["21:10"]),
            CreateClock());

        Assert.Contains(edit.Errors, error =>
            error.Key == "CreateSlipInput.OpenedAt" &&
            error.Message == "入店時刻に未来時刻は指定できません。");
    }

    private static CreateSlipEditContext CreateValidContext(
        IReadOnlyCollection<string>? timeOptions = null)
    {
        return new CreateSlipEditContext(
            new StoreBusinessDay
            {
                BusinessDayId = 1,
                BusinessDate = BusinessDate
            },
            BusinessDate,
            new StoreContext { DepartmentId = 1 },
            [new StoreTableOption { TableId = 10, TableCode = "A-1" }],
            [new NominationBackMasterItem { NominationKind = "regular" }],
            [new StoreOrderAttendanceCastOption { CastId = 20, DisplayName = "佐藤" }],
            timeOptions ?? ["20:00"],
            IsPreviousBusinessDayOpen: false,
            CanCreateSalesInput: true);
    }

    private static StoreClock CreateClock()
    {
        return new StoreClock(new FixedTimeProvider(
            DateTimeOffset.Parse("2026-07-30T12:00:00+00:00")));
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
