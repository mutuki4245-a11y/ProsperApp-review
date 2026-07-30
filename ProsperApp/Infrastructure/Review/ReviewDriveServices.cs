using System.Text;

namespace ProsperApp.Infrastructure.GoogleDrive;

public sealed class ReviewGoogleDriveAuthService : IGoogleDriveAuthService
{
    public bool IsGoogleAuthConfigured => true;
    public bool HasAccessRestriction => true;
    public string? ConfigurationErrorMessage => null;
    public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("review-token");
    public Task<bool> HasAccessTokenAsync() => Task.FromResult(true);
    public void ClearAccessToken()
    {
    }
}

public sealed class ReviewDriveFileService : IDriveFileService
{
    public Task<DriveFileResult> GetFileWithDiagnosticsAsync(string driveFileId, CancellationToken ct)
    {
        var html =
            $$"""
            <!doctype html>
            <html lang="ja">
            <head>
                <meta charset="utf-8">
                <style>
                    body { font-family: sans-serif; margin: 2rem; color: #1f2933; }
                    .receipt { max-width: 30rem; border: 1px solid #cbd5e1; padding: 1.5rem; }
                    .amount { font-size: 2rem; font-weight: 700; }
                </style>
            </head>
            <body>
                <div class="receipt">
                    <h1>レビュー用証憑プレビュー</h1>
                    <p>Drive file id: {{System.Net.WebUtility.HtmlEncode(driveFileId)}}</p>
                    <p class="amount">8,800円</p>
                    <p>このプレビューはレビュー用モックです。Google Drive には接続していません。</p>
                </div>
            </body>
            </html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);
        return Task.FromResult(DriveFileResult.Success(
            new DriveFileContent(new MemoryStream(bytes), "text/html; charset=utf-8", $"{driveFileId}.html")));
    }

    public void RemoveCachedFile(string driveFileId)
    {
    }
}
