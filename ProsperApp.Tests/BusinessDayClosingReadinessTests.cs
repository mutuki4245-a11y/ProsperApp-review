
namespace ProsperApp.Tests;

public class BusinessDayClosingReadinessTests
{
    [Fact]
    public void CanClose_WhenEveryClosingConditionIsSatisfied()
    {
        var readiness = CreateReadyReadiness();

        Assert.True(readiness.CanClose);
        Assert.Empty(readiness.BlockReasons);
    }

    [Fact]
    public void BlockReasons_ListsEveryFailedClosingCondition()
    {
        var readiness = new BusinessDayClosingReadiness
        {
            BusinessDay = CreateBusinessDay(),
            OpenSlipCount = 2,
            IsDrinkDeliveryAmountEntered = false,
            AttendanceCount = 3,
            MissingClockOutCount = 1,
            CastSalesMissingSlipCount = 1,
            ReceiptsEnabled = true,
            PendingReceiptCount = 4
        };

        Assert.False(readiness.CanClose);
        Assert.Equal(
            [
                "未会計伝票が 2 件あります。",
                "酒代が未入力です。酒代がない場合も0円で保存してください。",
                "退勤時刻が未入力のキャストが 1 名います。",
                "キャスト売上額調整が未完了です。",
                "未入力領収書が 4 件あります。"
            ],
            readiness.BlockReasons);
    }

    [Fact]
    public void BlockReasons_ShowsNoAttendanceInsteadOfMissingClockOut()
    {
        var readiness = CreateReadyReadiness(attendanceCount: 0, missingClockOutCount: 2);

        Assert.False(readiness.CanClose);
        Assert.Equal(
            ["勤怠入力に出勤キャストが登録されていません。"],
            readiness.BlockReasons);
    }

    [Fact]
    public void BlockReasons_WhenNoBusinessDay_ReturnsOnlyBusinessDayReason()
    {
        var readiness = new BusinessDayClosingReadiness
        {
            OpenSlipCount = 2,
            IsDrinkDeliveryAmountEntered = false,
            AttendanceCount = 0
        };

        Assert.False(readiness.CanClose);
        Assert.Equal(
            ["営業中の営業日がありません。最初の業務入力で営業日が自動作成されます。"],
            readiness.BlockReasons);
    }

    [Fact]
    public void StoreDecisionAndReasons_OverrideLocallyCalculatedConditions()
    {
        var readiness = CreateReadyReadiness(
            canCloseFromStore: false,
            blockReasonsFromStore: ["DB判定により締めできません。"]);

        Assert.False(readiness.CanClose);
        Assert.Equal(["DB判定により締めできません。"], readiness.BlockReasons);
    }

    private static BusinessDayClosingReadiness CreateReadyReadiness(
        int attendanceCount = 1,
        int missingClockOutCount = 0,
        bool? canCloseFromStore = null,
        IReadOnlyList<string>? blockReasonsFromStore = null)
    {
        return new BusinessDayClosingReadiness
        {
            BusinessDay = CreateBusinessDay(),
            OpenSlipCount = 0,
            IsDrinkDeliveryAmountEntered = true,
            AttendanceCount = attendanceCount,
            MissingClockOutCount = missingClockOutCount,
            CastSalesMissingSlipCount = 0,
            ReceiptsEnabled = true,
            PendingReceiptCount = 0,
            CanCloseFromStore = canCloseFromStore,
            BlockReasonsFromStore = blockReasonsFromStore ?? []
        };
    }

    private static StoreBusinessDay CreateBusinessDay()
    {
        return new StoreBusinessDay
        {
            BusinessDayId = 1,
            BusinessDate = new DateOnly(2026, 7, 30)
        };
    }
}
