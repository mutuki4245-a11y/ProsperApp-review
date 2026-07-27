using System.Globalization;

namespace ProsperApp.Models;

public static class StoreUiText
{
    private static readonly CultureInfo JapaneseCulture = CultureInfo.GetCultureInfo("ja-JP");

    public static string Amount(decimal value)
    {
        return value.ToString("N0", JapaneseCulture);
    }

    public static string Yen(decimal value)
    {
        return $"{Amount(value)} 円";
    }

    public static string Quantity(decimal value)
    {
        return value.ToString("0.##", JapaneseCulture);
    }

    public static string NumberInputValue(decimal value)
    {
        return value.ToString("0", CultureInfo.InvariantCulture);
    }
}
