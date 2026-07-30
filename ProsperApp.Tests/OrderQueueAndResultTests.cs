using ProsperApp.Features.Shared;
using ProsperApp.Services;
using ProsperApp.Pages;

namespace ProsperApp.Tests;

public class OrderQueueAndResultTests
{
    [Fact]
    public void ReadPostedQueue_MergesEquivalentLines()
    {
        var service = new OrderQueueService();

        var result = service.ReadPostedQueue(
            """
            [
              {"slipId":1,"itemId":20,"quantity":2,"castBackCastId":5},
              {"slipId":1,"itemId":20,"quantity":3,"castBackCastId":5}
            ]
            """,
            []);

        var line = Assert.Single(result);
        Assert.Equal(5, line.Quantity);
    }

    [Fact]
    public void ValidateSlipOrderQueue_RejectsNonAttendingBackCast()
    {
        var service = new OrderQueueService();
        var errors = service.ValidateSlipOrderQueue(
            [new OrderQueueInputModel { SlipId = 1, ItemId = 20, Quantity = 1, CastBackCastId = 99 }],
            [new StoreOrderItemOption { ItemId = 20, IsCastBackTarget = true }],
            [new StoreOrderAttendanceCastOption { CastId = 5 }]);

        Assert.Contains("バック対象商品のキャストは出勤キャストから選択してください。", errors);
    }

    [Fact]
    public void Result_DistinguishesSuccessfulEmptyDataFromFailure()
    {
        var empty = Result<IReadOnlyList<string>>.Success([]);
        var failure = Result<IReadOnlyList<string>>.Failure(
            ResultFailureKind.Unavailable,
            "取得失敗");

        Assert.True(empty.Succeeded);
        Assert.Empty(empty.Value);
        Assert.False(failure.Succeeded);
        Assert.Equal(ResultFailureKind.Unavailable, failure.FailureKind);
    }

    [Fact]
    public void ClosingReadiness_UsesAuthoritativeStoreDecision()
    {
        var readiness = new BusinessDayClosingReadiness
        {
            BusinessDay = new StoreBusinessDay
            {
                BusinessDayId = 1,
                BusinessDate = new DateOnly(2026, 7, 30)
            },
            IsDrinkDeliveryAmountEntered = true,
            AttendanceCount = 1,
            CanCloseFromStore = false,
            BlockReasonsFromStore = ["DBで締めを停止しました。"]
        };

        Assert.False(readiness.CanClose);
        Assert.Equal(["DBで締めを停止しました。"], readiness.BlockReasons);
    }

    [Fact]
    public void DeleteConfirmation_IncludesTheSelectedStoreName()
    {
        Assert.Equal("削除 Prosper本店", SettingsModel.BuildDeleteConfirmation("Prosper本店"));
    }
}
