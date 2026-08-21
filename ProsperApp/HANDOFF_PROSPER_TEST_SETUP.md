# prosper-test セットアップ引き継ぎ

Supabaseテスト用プロジェクトの残作業。**psqlでSupabaseに到達できる環境**が必要。
（Claude Code on the web のコンテナは外向きがHTTPSプロキシのみで、5432が塞がっていて実行できなかった）

## 対象

| | 値 |
|---|---|
| プロジェクト名 | `prosper-test` |
| project ref | `flwjmvaysjcxrtshvyli` |
| リージョン | ap-southeast-2 |
| API URL | `https://flwjmvaysjcxrtshvyli.supabase.co` |
| リポジトリ | ProsperApp / ブランチ `claude/review-branch-setup-o050l2` |

**本番 `zwdecfoecgpzpkallukh` には触らないこと。** 以下は全て prosper-test に対する作業。

## 済んでいること

- プロジェクト作成（free枠、$0/月）
- 拡張導入: `pg_cron` 1.6.4 / `pgtap` 1.3.3 / `pgcrypto` 1.3
  （`pgtap` は `extensions` スキーマ。pgTAPテストの `search_path` がそこを見るため）
- ロール `anon` / `authenticated` / `service_role` 作成済み
- `Sql/baseline/00_supabase_roles.sql`、`00_company_department_master.sql`、
  `01_accounting_tables.sql` を適用済み（public 2テーブル / accounting 8テーブル）

適用済みの分も含めて **`apply_all.sh` は全ファイル冪等**なので、下の手順1をそのまま
頭から流して問題ない。途中から再開する必要はない。

## 手順1: スキーマ投入

接続文字列はダッシュボードの Settings > Database から取得する。

```bash
git clone -b claude/review-branch-setup-o050l2 https://github.com/mutuki4245-a11y/ProsperApp
cd ProsperApp
ProsperApp/Sql/apply_all.sh --with-test-fixtures \
  "postgresql://postgres:<PASSWORD>@db.flwjmvaysjcxrtshvyli.supabase.co:5432/postgres"
```

- `--with-test-fixtures` は必須。付けないと商品が0件で、伝票から先へ進めない
- 50ファイルが順に流れる。`ON_ERROR_STOP=1` なので失敗したらそこで止まる
- 2回流しても壊れない。むしろ `quick_entry_account_master_updates.sql` は
  補助科目の紐付けが1回目で0件になる仕様なので、**2回流すのが正しい**

## 手順2: 適用結果の検証

```bash
psql "<同じ接続文字列>" --set ON_ERROR_STOP=1 \
  -f ProsperApp/Sql/tests/security_and_retention_pgtap.sql
```

pgTAP 11件が全て `ok` になること。CIの `database` job と同じもの。

オブジェクト数の目安（ローカル検証・CI実測値）:

| 対象 | 期待値 |
|---|---|
| `store` スキーマの関数 | 131 |
| public + store + accounting のテーブル | 43 |
| 商品マスタ (`store_item_master`) | 10 |
| キャスト / スタッフ / テーブル / 料金プラン | 3 / 2 / 18 / 1 |

## 手順3: RPC実行ロールのパスワード設定

`Sql/store_rpc/99_grants.sql` が `prosper_rpc_executor` を作るが、パスワードは設定しない。

```sql
alter role prosper_rpc_executor password '<生成した値>';
```

## 手順4: Edge Function の配置

```bash
supabase functions deploy prosper-rpc \
  --project-ref flwjmvaysjcxrtshvyli \
  --no-verify-jwt
```

`--no-verify-jwt` は必須。この関数はJWTではなく独自のAPIキー認証を実装している。

配置後、Edge Function secrets を設定する。

| secret | 値 |
|---|---|
| `PROSPER_RPC_DB_URL` | 手順3の `prosper_rpc_executor` で接続する接続文字列 |
| `ProsperApp_API_KEY` | 新規生成。**本番と同じ値を使わないこと** |

## 手順5: レビュー用 App Service の設定値

これが最終成果物。Azure App Service のアプリケーション設定に入れる。

| 名前 | 値 |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Staging` |
| `SUPABASE_RPC_EDGE_FUNCTION_URL` | `https://flwjmvaysjcxrtshvyli.supabase.co/functions/v1/prosper-rpc` |
| `Supabase_Edge_Key` | 手順4の `ProsperApp_API_KEY` と同じ値 |
| `GoogleDrive__ClientId` / `GoogleDrive__ClientSecret` | レビュー用OAuthクライアント |

`appsettings.Staging.json` は `Supabase:Url` を空にしてある。環境変数を入れ忘れた
レビュー環境は本番に繋がるのではなく、どこにも繋がらずエラーになる。

残りの手順（App Service作成、publish profileのSecret登録、Google OAuthの
リダイレクトURI追加）は `ProsperApp/docs/review-environment.md` にある。

## 注意

- ログインできるのは `Sql/store_rpc/00b_app_access.sql` が登録する2アカウントのみ
- `mieu_honten_product_master_seed.sql` は `apply_all.sh` の対象外。本番の既存状態を
  検証する一度きりの移行スクリプトで、空のDBでは必ず落ちる。テスト用カタログは
  `Sql/fixtures/test_catalog.sql` が担当する
