using System.Text.Json;
using ProsperApp.Models;

namespace ProsperApp.Services;

public static class OrderQueueJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static List<OrderQueueInputModel> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            var lines = JsonSerializer.Deserialize<List<PostedOrderQueueLine>>(value, Options);
            return lines?
                .Where(x => x.ItemId > 0 && x.Quantity > 0)
                .Select(x => new OrderQueueInputModel
                {
                    SlipId = x.SlipId,
                    ItemId = x.ItemId,
                    Quantity = x.Quantity,
                    CastBackCastId = x.CastBackCastId
                })
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed class PostedOrderQueueLine
    {
        public long? SlipId { get; set; }

        public long ItemId { get; set; }

        public int Quantity { get; set; }

        public long? CastBackCastId { get; set; }
    }
}
