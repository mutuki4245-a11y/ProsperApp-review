using ProsperApp.Options;

namespace ProsperApp.Services;

internal static class SupabaseApiKeyHeaders
{
    public static string ReadAccessKey(SupabaseOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.SecretKey)
            ? options.SecretKey
            : options.PublishableKey;
    }

    public static string MutationAccessKey(SupabaseOptions options)
    {
        return options.SecretKey;
    }

    public static bool HasReadAccess(SupabaseOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.Url) &&
               !string.IsNullOrWhiteSpace(ReadAccessKey(options));
    }

    public static bool HasMutationAccess(SupabaseOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.Url) &&
               !string.IsNullOrWhiteSpace(MutationAccessKey(options));
    }

    public static void Apply(HttpRequestMessage request, string accessKey)
    {
        request.Headers.Add("apikey", accessKey);
    }
}
