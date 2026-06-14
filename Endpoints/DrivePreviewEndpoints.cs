using System.Text.Json;
using ProsperApp.Services;

namespace ProsperApp.Endpoints;

public static class DrivePreviewEndpoints
{
    public static IEndpointRouteBuilder MapDrivePreviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/DrivePreview/{driveFileId}", HandleAsync)
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task HandleAsync(
        HttpContext context,
        string driveFileId,
        bool? prefetch,
        IDriveFileService driveFileService,
        CancellationToken cancellationToken)
    {
        var shouldPrefetch = prefetch == true;
        var result = shouldPrefetch
            ? await driveFileService.PrefetchFileAsync(driveFileId, cancellationToken)
            : await driveFileService.GetFileWithDiagnosticsAsync(driveFileId, cancellationToken);

        if (result.Content is null)
        {
            if (IsAuthenticationFailure(result.ErrorCode))
            {
                if (shouldPrefetch)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                await WriteGoogleLoginRedirectHtmlAsync(context, cancellationToken);
                return;
            }

            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                $"Driveプレビューを表示できません。\nエラー: {result.ErrorCode}\n内容: {result.ErrorMessage}\nFileId: {driveFileId}",
                cancellationToken);
            return;
        }

        await using var stream = result.Content.Stream;
        if (shouldPrefetch)
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        context.Response.ContentType = result.Content.ContentType;
        context.Response.Headers.ContentDisposition =
            $"inline; filename*=UTF-8''{Uri.EscapeDataString(result.Content.FileName)}";
        await stream.CopyToAsync(context.Response.Body, cancellationToken);
    }

    private static bool IsAuthenticationFailure(string? errorCode)
    {
        return errorCode is "missing_access_token" ||
               errorCode?.EndsWith("_401", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static async Task WriteGoogleLoginRedirectHtmlAsync(HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        var loginUrl = $"/Login?returnUrl={Uri.EscapeDataString(BuildPreviewReturnUrl(context))}&forceGoogle=true";
        var html =
            $$"""
            <!doctype html>
            <html lang="ja">
            <head>
                <meta charset="utf-8">
                <title>Google認証が必要です</title>
            </head>
            <body style="font-family: sans-serif; padding: 1rem;">
                <p>Google認証が必要です。Googleログインへ進みます。</p>
                <script>
                    window.top.location.href = {{JsonSerializer.Serialize(loginUrl)}};
                </script>
            </body>
            </html>
            """;

        await context.Response.WriteAsync(html, cancellationToken);
    }

    private static string BuildPreviewReturnUrl(HttpContext context)
    {
        const string fallback = "/Closing/Receipts";
        var referer = context.Request.Headers.Referer.ToString();
        if (!Uri.TryCreate(referer, UriKind.Absolute, out var uri))
        {
            return fallback;
        }

        if (!string.Equals(uri.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        if (context.Request.Host.Port is { } port && uri.Port != port)
        {
            return fallback;
        }

        return uri.PathAndQuery;
    }
}
