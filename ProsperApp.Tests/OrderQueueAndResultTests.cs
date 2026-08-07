using ProsperApp.Features.Shared;
using ProsperApp.Services;
using ProsperApp.Pages;

namespace ProsperApp.Tests;

public class OrderQueueAndResultTests
{
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
