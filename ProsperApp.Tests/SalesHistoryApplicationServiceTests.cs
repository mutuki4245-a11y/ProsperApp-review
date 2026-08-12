using ProsperApp.Features.SalesHistory;

namespace ProsperApp.Tests;

public class SalesHistoryApplicationServiceTests
{
    [Fact]
    public async Task GetPageAsync_RejectsReversedDateRangeWithoutCallingRepository()
    {
        var repository = new FakeSalesHistoryRepository();
        var service = new SalesHistoryApplicationService(repository);

        var result = await service.GetPageAsync(
            new SalesHistoryQuery(new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 1), null, null),
            new DateOnly(2026, 8, 3),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultFailureKind.InvalidInput, result.FailureKind);
        Assert.Equal(0, repository.CallCount);
    }

    [Fact]
    public async Task SaveCorrectionAsync_AllowsNullHistoricalCastSalesAmounts()
    {
        var repository = new FakeSalesHistoryRepository();
        var service = new SalesHistoryApplicationService(repository);
        var mutation = new SalesHistoryCorrectionMutation
        {
            OperationId = Guid.NewGuid().ToString("D"),
            BusinessDayId = 10,
            ExpectedSnapshotVersion = 2,
            CurrentBusinessDate = new DateOnly(2026, 8, 3),
            ActorEmail = "operator@example.com",
            Reason = "締めメモの補正",
            DrinkDeliveryAmount = 0,
            CastSalesSlips =
            [
                new SalesHistoryCastSalesSlipMutation(
                    20,
                    "total",
                    "split",
                    [new SalesHistoryCastSalesMutation(30, null)])
            ]
        };

        var result = await service.SaveCorrectionAsync(mutation, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, repository.CallCount);
    }

    [Fact]
    public async Task SaveCorrectionAsync_RejectsReasonOverFiveHundredCharacters()
    {
        var repository = new FakeSalesHistoryRepository();
        var service = new SalesHistoryApplicationService(repository);
        var mutation = new SalesHistoryCorrectionMutation
        {
            OperationId = Guid.NewGuid().ToString("D"),
            BusinessDayId = 10,
            ExpectedSnapshotVersion = 2,
            CurrentBusinessDate = new DateOnly(2026, 8, 3),
            ActorEmail = "operator@example.com",
            Reason = new string('a', 501),
            DrinkDeliveryAmount = 0
        };

        var result = await service.SaveCorrectionAsync(mutation, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultFailureKind.InvalidInput, result.FailureKind);
        Assert.Equal(0, repository.CallCount);
    }

    private sealed class FakeSalesHistoryRepository : ISalesHistoryRepository
    {
        public int CallCount { get; private set; }

        public Task<Result<SalesHistoryPage>> GetPageAsync(
            SalesHistoryQuery query,
            DateOnly currentBusinessDate,
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(Result<SalesHistoryPage>.Success(new SalesHistoryPage()));
        }

        public Task<Result<SalesHistoryCorrectionEditor>> GetCorrectionEditorAsync(
            long businessDayId,
            DateOnly currentBusinessDate,
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(Result<SalesHistoryCorrectionEditor>.Success(
                new SalesHistoryCorrectionEditor { BusinessDayId = businessDayId }));
        }

        public Task<Result<SalesHistoryCorrectionResult>> SaveCorrectionAsync(
            SalesHistoryCorrectionMutation mutation,
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(Result<SalesHistoryCorrectionResult>.Success(
                new SalesHistoryCorrectionResult
                {
                    Status = "unchanged",
                    OperationId = mutation.OperationId!,
                    BusinessDayId = mutation.BusinessDayId,
                    SnapshotVersion = mutation.ExpectedSnapshotVersion
                }));
        }
    }
}
