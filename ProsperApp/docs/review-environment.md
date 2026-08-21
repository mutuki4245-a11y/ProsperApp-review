# レビュー環境（外部公開 + テスト用DB）

`claude/review-branch-setup-o050l2` を、本番とは別のAzure App Serviceへ公開し、
本番とは別のSupabaseプロジェクトへ繋ぐための手順です。

本番 (`prosper-web` / Supabase `zwdecfoecgpzpkallukh`) には一切触れません。

## 全体像

| | 本番 | レビュー |
|---|---|---|
| App Service | `prosper-web` | 新規に作成する |
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Staging` |
| 設定ファイル | `appsettings.json` | `appsettings.Staging.json` |
| Supabase | `zwdecfoecgpzpkallukh` | 新規に作成する |
| 画面上の表示 | バナーなし | 「テスト環境」バナー |
| 配信方法 | publish profileによる直接デプロイ | `deploy-review.yml` によるpush配信 |

`appsettings.Staging.json` は `Supabase:Url` を空にしています。接続先の環境変数を
入れ忘れたレビュー環境は、本番へ繋がるのではなく、どこにも繋がらずエラーになります。

## 1. テスト用Supabaseプロジェクト

### 1-1. プロジェクトを作る

現在のorganizationはfreeプランで、**同時に有効なプロジェクトは2つまで**です。
既に `zwdecfoecgpzpkallukh`（本番）と `cast_race_newbee` の2つが埋まっているため、
このままでは3つ目を作れません。次のどれかが必要です。

- `cast_race_newbee` を一時停止する（再開可能）
- `cast_race_newbee` を削除する
- organizationをProプランへ上げる

作成時のリージョンは、本番と挙動を揃えるため `ap-southeast-2` を推奨します。

### 1-2. 拡張を有効にする

Supabaseのダッシュボード（Database → Extensions）で有効化します。

- `pg_cron` — 保持期間の定期実行に使います
- `pgtap` — `Sql/tests/` を実行する場合に使います

### 1-3. スキーマを流す

テスト用プロジェクトの接続文字列（Settings → Database → Connection string）を使います。

```bash
ProsperApp/Sql/apply_all.sh --with-test-fixtures "postgresql://postgres:<password>@<host>:5432/postgres"
```

`--with-test-fixtures` を付けると、動作確認用のカタログ（商品、キャスト、スタッフ、
料金プラン）まで入ります。これを付けないと商品が1つも無く、伝票から先へ進めません。

本番の商品マスタseed `mieu_honten_product_master_seed.sql` は、既存カテゴリと
既存商品の値まで検証する一度きりの移行スクリプトなので、空のDBには当てられません。
`apply_all.sh` の対象からも外してあります。

なお `quick_entry_account_master_updates.sql` は、補助科目の登録と勘定科目への
紐付けを1つのSQL文で行うため、1回目の実行では紐付けが0件になります。
`apply_all.sh` をもう一度流すと揃います。

### 1-4. RPC実行ロールにパスワードを設定する

`Sql/store_rpc/99_grants.sql` が `prosper_rpc_executor` ロールを作りますが、
パスワードは設定しません。Edge Functionから接続するために設定します。

```sql
alter role prosper_rpc_executor password '<生成したパスワード>';
```

### 1-5. Edge Functionを配信する

```bash
supabase functions deploy prosper-rpc \
  --project-ref <テスト用project-ref> \
  --no-verify-jwt
```

配信後、テスト用プロジェクトのEdge Function secretsに設定します。

| secret | 内容 |
|---|---|
| `PROSPER_RPC_DB_URL` | `prosper_rpc_executor` で接続する接続文字列 |
| `ProsperApp_API_KEY` | アプリから送るAPIキー。**本番と同じ値を使わないこと** |

## 2. レビュー用のAzure App Service

### 2-1. App Serviceを作る

本番とは別のApp Serviceを新規に作成します。F1（無料）で足ります。
ランタイムは本番と同じ .NET 10 / Linux を選びます。

### 2-2. アプリケーション設定

App Service → 設定 → 環境変数に入れます。

| 名前 | 値 |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Staging` |
| `SUPABASE_RPC_EDGE_FUNCTION_URL` | `https://<テスト用ref>.supabase.co/functions/v1/prosper-rpc` |
| `Supabase_Edge_Key` | 1-5で設定した `ProsperApp_API_KEY` と同じ値 |
| `GoogleDrive__ClientId` | レビュー用のOAuthクライアントID |
| `GoogleDrive__ClientSecret` | レビュー用のOAuthクライアントシークレット |

`Supabase_Edge_Key` は本番と別の値にします。レビュー環境の設定が漏れても、
本番のEdge Functionは叩けません。

### 2-3. Google OAuthのリダイレクトURI

Google Cloud ConsoleのOAuthクライアントに、レビュー環境のURLを追加します。

```
https://<レビュー用App Service名>.azurewebsites.net/signin-google
```

これを忘れると、レビュー環境でログインした時点で `redirect_uri_mismatch` になります。

### 2-4. ログインできるユーザー

`Sql/store_rpc/00b_app_access.sql` が登録する2アカウントだけがログインできます。
別のレビュアーを入れる場合は、テスト用DBに対してだけ追加してください。

## 3. GitHubの設定

| 種類 | 名前 | 内容 |
|---|---|---|
| secret | `AZURE_REVIEW_PUBLISH_PROFILE` | レビュー用App Serviceのpublish profile（XMLそのまま） |
| variable | `AZURE_REVIEW_APP_NAME` | レビュー用App Serviceの名前 |

publish profileはApp Serviceの「発行プロファイルの取得」から取れます。
本番 `prosper-web` のprofileをここへ入れないでください。

両方が揃うまで `deploy-review.yml` は配信をスキップし、失敗扱いにはなりません。

## 4. 動作確認

1. reviewブランチへpushする
2. Actionsの "Deploy review environment" が成功する
3. レビュー環境のURLを開き、最上部に「テスト環境」バナーが出る
4. ログインして営業中トップが表示され、伝票→注文→会計→締めまで通る
5. 本番URLを開き、バナーが**出ない**ことを確認する

## テスト用DBはmainでも使う

レビュー環境とは別に、CIが毎回使い捨てのPostgreSQLを立ち上げます
（`.github/workflows/ci.yml` の `database` job）。

- `Sql/` 一式だけで空のDBを組み立てられることを確認する
- 2回流して、全ファイルが冪等であることを確認する
- `Sql/tests/security_and_retention_pgtap.sql` を実行する

こちらはSupabaseのプロジェクト数を消費せず、main / reviewの両方で回ります。
