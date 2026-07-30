using System.Text.Json;
using ProsperApp.Features.BusinessHome;
using ProsperApp.Models;

namespace ProsperApp.Tests;

public class BusinessHomeOperationContractTests
{
    [Fact]
    public void Parse_ReturnsTypedPayloadForValidOrder()
    {
        var operation = CreateOperation(
            BusinessHomeOperationTypes.AddOrder,
            """{"item_id":12,"quantity":3,"cast_back_cast_id":45}""");

        var result = BusinessHomeOperationParser.Parse(operation);

        var payload = Assert.IsType<AddOrderPayload>(result.Value);
        Assert.True(result.Succeeded);
        Assert.Equal(12, payload.ItemId);
        Assert.Equal(3, payload.Quantity);
        Assert.Equal(45, payload.CastBackCastId);
    }

    [Theory]
    [InlineData("unknown", """{}""")]
    [InlineData("add_order", """{"item_id":0,"quantity":1}""")]
    [InlineData("add_nomination", """{"cast_id":1,"nomination_kind":"regular","nomination_price":1500}""")]
    public void Parse_RejectsUnknownOrInvalidOperations(string operationType, string json)
    {
        var result = BusinessHomeOperationParser.Parse(CreateOperation(operationType, json));

        Assert.False(result.Succeeded);
        Assert.Equal("保存内容を確認してください。", result.ErrorMessage);
    }

    private static BusinessSlipEditorOperationInput CreateOperation(string operationType, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new BusinessSlipEditorOperationInput
        {
            OperationId = "operation-1",
            SlipId = 1,
            OperationType = operationType,
            Payload = document.RootElement.Clone()
        };
    }
}
