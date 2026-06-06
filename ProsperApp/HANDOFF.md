# ProsperApp Handoff

## 現在の位置づけ

このアプリは店舗用アプリのコード基盤です。
領収書簡易入力は一区切りし、現在は店舗アプリ内の `締め作業 > 領収書入力` 機能として扱います。
今後の中心機能は、開け作業、営業中の伝票管理、会計、締め作業です。

## 重要方針

- DB操作は原則 Supabase RPC 経由で行います。
- アプリ側から直接テーブルRESTを叩く実装は避けます。
- RLSは有効化し、アプリ用の操作は `security definer` RPCで制御します。
- 店舗は `department_master.department_id` を基準に扱います。
- 端末ごとの店舗設定はブラウザ `localStorage` と通常Cookieに保存します。
- サーバー側処理ではCookieの `StoreDepartmentId` を優先し、なければ `appsettings` の `Supabase:StoreDepartmentId` にフォールバックします。
- Google Driveプレビューはアプリサーバー経由で取得します。
- 秘密情報、ServiceRoleKey、Google認証情報はローカル設定に保存しません。

## 参照すべきSQLファイル

- `Sql/agent_schema_reference.sql`
  - 次のエージェント向けの参照用スキーマ集約ファイルです。
  - 実行用ではありません。

- `Sql/store_order_accounting_tables.sql`
  - 店舗営業、伝票、客行、指名、注文、会計系テーブルの作成SQLです。
  - RLS有効化、updated_atトリガー、主要インデックスを含みます。

- `Sql/store_settings_functions.sql`
  - 店舗設定画面用の `get_store_departments()` RPCです。
  - `department_master` から有効店舗一覧を取得します。

- `Sql/store_rpc_functions.sql`
  - 営業日、伝票作成、領収書入力などのアプリ操作用RPCです。
  - アプリのDB操作方針上、重要な実行対象です。

- `Sql/quick_entry_account_master_updates.sql`
  - 領収書簡易入力UIで使う科目・補助科目の追加更新SQLです。
  - 文字化けが残っている可能性があるため、実行前に内容確認が必要です。

## Supabaseで実行が必要なSQL

本番/開発DBに未反映の場合、Supabase SQL Editorで以下を実行してください。

1. `Sql/store_order_accounting_tables.sql`
2. `Sql/store_settings_functions.sql`
3. `Sql/store_rpc_functions.sql`
4. 必要に応じて `Sql/quick_entry_account_master_updates.sql`

`agent_schema_reference.sql` は実行しないでください。

## 主要RPC

### 店舗設定

- `get_store_departments()`

### 店舗コンテキスト・営業日

- `get_store_context(p_department_id)`
- `get_current_business_day(p_department_id)`
- `open_business_day(p_department_id, p_business_date, p_memo)`
- `get_open_slip_count(p_department_id, p_business_day_id)`
- `close_business_day(p_department_id, p_business_day_id, p_memo)`

### 伝票

- `get_store_tables(p_department_id)`
- `get_store_casts(p_department_id)`
- `get_business_day_slips(p_department_id, p_business_day_id)`
- `create_store_slip(p_department_id, p_table_id, p_opened_at, p_customer_labels, p_cast_ids, p_memo)`

### 領収書

- `get_pending_receipts(p_department_id, p_status)`
- `quick_enter_receipt(p_department_id, p_document_id, p_payment_date, p_amount, p_account_subject, p_description, p_group_code, p_status)`
- `mark_receipt_scan_mistake(p_department_id, p_document_id, p_status)`

## 画面構成

- `/`
  - 店舗ホーム。
  - 開け作業、営業中、締め作業への導線を置く方針です。

- `/Opening`
  - 開け作業の営業開始画面。
  - 現在のステップ方針は `キャスト情報編集 -> 営業開始` のみです。

- `/Opening/Casts`
  - キャスト情報確認/編集導線用。
  - 現時点では登録済みキャスト確認が中心です。

- `/Slips/Create`
  - 伝票起こし。
  - 入店時刻は5分単位。
  - 客数は直接入力せず、客名入力行の数で扱います。
  - 営業日が開いていない場合は作成不可です。

- `/Closing`
  - 締め作業ホーム。
  - 今後のステップは、酒代入力、勤怠入力、キャスト売上額調整、領収書入力です。

- `/Closing/Receipts`
  - 領収書簡易入力。
  - Google Driveプレビュー、Supabase RPC更新、スキャンミス除外、PDF先読みキャッシュを含みます。

- `/Settings`
  - 管理者設定。
  - パスワードは固定で `4245`。
  - 設定値は端末ローカル保存です。

## Azure / 環境変数

Azure App Serviceでは最低限以下が必要です。

- `Supabase__Url`
- `Supabase__AnonKey`

RPCが `grant execute to anon, authenticated` 済みなら一覧取得などはAnonKeyで動きます。
ただし、運用上ServiceRoleKeyを使う実装や直接操作が残っている場合は以下も必要です。

- `Supabase__ServiceRoleKey`

Google Drive OAuth/プレビューを使う場合は以下も必要です。

- `GoogleDrive__ClientId`
- `GoogleDrive__ClientSecret`
- `GoogleDrive__Scopes__0` など

## 注意点

- `AGENTS.md` は現在PowerShell表示上で文字化けして見える場合があります。UTF-8前提で扱ってください。
- `Sql/quick_entry_account_master_updates.sql` には実際に文字化けした日本語が残っている可能性があります。実行前に修正してください。
- サブエージェントには単純な調査・軽量レビュー・低リスク編集だけを委任します。
- SQL/RPC設計、会計、給与、認証、RLS、Google Drive権限まわりの判断はメインCodexが行います。
- このフォルダは現時点でgitリポジトリとして認識されない可能性があります。`git diff` 前提で作業しないでください。

## 次に着手しやすい作業

1. Supabaseで `store_rpc_functions.sql` が実行済みか確認する。
2. `quick_entry_account_master_updates.sql` の文字化けを修正する。
3. 営業中ホームを「伝票起こしボタン + 当日伝票一覧 + 詳細/会計ボタン」に寄せる。
4. 締め作業をステップ式に整理する。
5. 伝票編集、会計処理、注文入力のRPCと画面を追加する。
