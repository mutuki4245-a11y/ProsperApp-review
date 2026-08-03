using System.Text.Json;

namespace ProsperApp.Infrastructure.Supabase;

internal static class StoreBootstrapJson
{
    public static IReadOnlyList<JsonElement> ReadArray(JsonElement row, string propertyName)
    {
        if (!row.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray().Select(item => item.Clone()).ToList();
    }

    public static bool TryReadObject(JsonElement row, string propertyName, out JsonElement value)
    {
        if (row.TryGetProperty(propertyName, out var candidate) && candidate.ValueKind == JsonValueKind.Object)
        {
            value = candidate.Clone();
            return true;
        }

        value = default;
        return false;
    }
}
