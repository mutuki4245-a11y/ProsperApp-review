using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProsperApp.Services;

namespace ProsperApp.Pages;

[Authorize]
public class DrivePreviewModel(IDriveFileService driveFileService) : PageModel
{
    private readonly IDriveFileService _driveFileService = driveFileService;

    public async Task<IActionResult> OnGetAsync(string driveFileId, bool prefetch, CancellationToken cancellationToken)
    {
        var result = prefetch
            ? await _driveFileService.PrefetchFileAsync(driveFileId, cancellationToken)
            : await _driveFileService.GetFileWithDiagnosticsAsync(driveFileId, cancellationToken);
        if (result.Content is null)
        {
            if (IsAuthenticationFailure(result.ErrorCode))
            {
                return Content(
                    """
                    <!doctype html>
                    <html lang="ja">
                    <head>
                        <meta charset="utf-8">
                        <title>Google Drive認証切れ</title>
                    </head>
                    <body style="font-family: sans-serif; padding: 1rem;">
                        <p>Google Driveの認証が切れている可能性があります。再ログインします。</p>
                        <script>
                            window.top.location.href = '/logout?returnUrl=/';
                        </script>
                    </body>
                    </html>
                    """,
                    "text/html");
            }

            return Content(
                $"Driveプレビューを表示できません。\nエラー: {result.ErrorCode}\n内容: {result.ErrorMessage}\nFileId: {driveFileId}",
                "text/plain");
        }

        if (prefetch)
        {
            result.Content.Stream.Dispose();
            return StatusCode(StatusCodes.Status204NoContent);
        }

        Response.Headers.ContentDisposition = $"inline; filename=\"{result.Content.FileName}\"";
        return File(result.Content.Stream, result.Content.ContentType);
    }

    private static bool IsAuthenticationFailure(string? errorCode)
    {
        return errorCode is "missing_access_token" ||
               errorCode?.EndsWith("_401", StringComparison.OrdinalIgnoreCase) == true;
    }
}

