# ProsperApp Handoff

## 現在の位置づけ

このアプリは店舗用アプリのコード基盤です。
領収書簡易入力は一区切りし、現在は店舗アプリ内の `締め作業 > 領収書入力` 機能として扱います。
今後の中心機能は、開け作業、営業中の伝票管理、会計、締め作業です。

## 重要方針

- DB操作は原則 Supabase RPC 経由で行います。
- アプリ側から直接テーブルRESTを叩く実装は避けます。
- Supabase RPCのHTTP送信、Edge Functionキー、レスポンスJSON配列/スカラー処理は `ISupabaseRpcClient` / `SupabaseRpcClient` に集約します。アプリからのRPCは必ず `prosper-rpc` Edge Function経由で呼び出し、REST RPC fallbackは持ちません。
- RLSは有効化し、アプリ用の操作は `security definer` RPCで制御します。
- 現場画面の初期表示では、既存RPCをPageModel内で並列化して待ち時間を短縮します。卓、商品、キャスト、店舗コンテキスト、店舗一覧などのマスタ系候補はサーバー側 `IMemoryCache` に初回成功時だけ保持し、商品/カテゴリ/キャストのマスタ設定保存が成功した場合だけ関連キャッシュを破棄します。現在営業日は店舗別に締め成功までキャッシュし、営業日開始時は更新、締め成功時は破棄します。複数インスタンスではプロセス単位のキャッシュになるため、他プロセスで締めた営業日は次回プロセス再起動または明示破棄まで残り得ます。RPC失敗や設定未完了の結果はキャッシュしません。
- 現場運用は、営業中画面を操作する `sales-management` 端末1台と、注文入力専用の `order-entry` / `/Orders` 端末複数台を前提にします。localStorageや画面内ドラフトは端末内の復旧用状態として扱い、端末間では直接同期しません。端末間の共有状態はDB/RPC保存後のデータを基準にします。
- 出勤キャスト候補の `get_order_attending_casts` は、変更契機が勤怠入力に限られるため営業日単位でキャッシュしてよいです。勤怠保存、営業日開始、営業日締めで破棄します。退勤済みキャストも候補に残す仕様なので、退勤済みかどうかだけを理由に候補キャッシュを避ける必要はありません。
- 営業中トップは営業中操作に必要な一覧だけを取得し、締め作業専用の酒代、締め勤怠、未処理領収書、キャスト売上額調整状態は `/Closing` で取得します。伝票追加モーダルの指名候補は初期表示をブロックせず、モーダル表示時にGET handlerで遅延取得します。営業中カラオケ自動保存は `businessDayId`、`slipId`、`quantity` を `save_store_karaoke_lines` へ送るだけにし、店舗コンテキスト、卓、伝票一覧は再取得しません。カラオケは `store_item_master.item_type = 'karaoke'` の商品として扱い、保存RPCは同一伝票内のカラオケ注文行を1行に集約します。
- 一覧RPCは対象営業日・対象伝票を先に絞ってから関連行を集計します。特に `get_business_day_slips` と `get_cast_sales_adjustment_slips` は全期間の客、指名、注文、自由入力明細を集計してから最後に絞る形へ戻さないでください。
- 店舗は `department_master.department_id` を基準に扱います。
- 端末ごとの店舗設定はブラウザ `localStorage` と通常Cookieに保存します。
- サーバー側処理ではCookieの `StoreDepartmentId` を優先し、なければ `appsettings` の `Supabase:StoreDepartmentId` にフォールバックします。
- アプリログインはGoogle認証に統一します。
- Googleログインの許可対象は `GoogleAuth:AllowedEmails` または `GoogleAuth:AllowedDomains` で明示します。
- Google Driveプレビューはアプリサーバー経由で取得します。
- 秘密情報、Supabase RPCキー、Google認証情報はローカル設定に保存しません。
- 動作確認は毎回Azureへデプロイした環境で行います。ローカル起動やローカル用 `appsettings` 整備は前提にしません。

## 参照すべきSQLファイル

- `Docs/SQL_RPC概要.md`
  - SQL定義とRPCの全体像を確認するための入口資料です。
  - 実際の定義は各SQLファイルで確認してください。

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
  - 分割済みRPCファイルの実行順を示す非実行インデックスです。
  - 実行対象は `Sql/store_rpc/*.sql` です。

- `Sql/store_rpc/01_business_day.sql`
  - 営業日、出勤、営業締め系RPCです。

- `Sql/store_rpc/02_store_masters.sql`
  - 卓番、キャスト、商品、注文入力向け一覧系RPCです。

- `Sql/store_rpc/03_slips.sql`
  - 伝票詳細、客、指名、注文取消系RPCです。

- `Sql/store_rpc/04_orders.sql`
  - 注文登録系RPCです。

- `Sql/store_rpc/05_checkout.sql`
  - 会計確定、伝票作成系RPCです。

- `Sql/store_rpc/06_receipts.sql`
  - 領収書入力、スキャンミス除外系RPCです。

- `Sql/store_rpc/07_cast_sales_adjustments.sql`
  - 締め作業のキャスト売上額調整系RPCです。

- `Sql/store_rpc/99_grants.sql`
  - アプリRPCの直接PostgREST実行権限を剥奪する現在定義です。

- `Sql/quick_entry_account_master_updates.sql`
  - 領収書簡易入力UIで使う科目・補助科目の追加更新SQLです。
  - 文字化けが残っている可能性があるため、実行前に内容確認が必要です。

- `Sql/store_table_master_seed.sql`
  - mieu本店の卓番マスタ初期データです。
  - `store_table_master` に卓番 `A1` から `A6`、`B1` から `B6`、`C1` から `C6` を登録します。

## SQL参照とDB反映

SQLファイルは現在のDB定義を確認するための参照資料です。DB定義の変更はCodexがSupabase CLIまたはSupabaseコネクタで実行し、実行後にSQLファイルを現在定義へ合わせます。

参照時の順序は以下です。

1. `Sql/store_order_accounting_tables.sql`
2. `Sql/store_settings_functions.sql`
3. `Sql/store_rpc/01_business_day.sql`
4. `Sql/store_rpc/02_store_masters.sql`
5. `Sql/store_rpc/03_slips.sql`
6. `Sql/store_rpc/04_orders.sql`
7. `Sql/store_rpc/05_checkout.sql`
8. `Sql/store_rpc/06_receipts.sql`
9. `Sql/store_rpc/07_cast_sales_adjustments.sql`
10. `Sql/store_rpc/99_grants.sql`
11. 必要に応じて `Sql/store_table_master_seed.sql`
12. 必要に応じて `Sql/quick_entry_account_master_updates.sql`

`agent_schema_reference.sql` と `store_rpc_functions.sql` は実行対象ではありません。

## 主要RPC

### 店舗設定

- `get_store_departments()`

### 店舗コンテキスト・営業日

- `get_store_context(p_department_id)`
- `get_current_business_day(p_department_id)`
- `open_business_day(p_department_id, p_business_date, p_memo)`
- `open_business_day_with_attendance(p_department_id, p_business_date, p_attendance_entries, p_memo)`
- `get_open_slip_count(p_department_id, p_business_day_id)`
- `get_business_day_drink_delivery_status(p_department_id, p_business_day_id)`
- `save_business_day_drink_delivery_amount(p_department_id, p_business_day_id, p_drink_delivery_amount)`
- `get_business_day_closing_attendance(p_department_id, p_business_day_id)`
- `save_business_day_closing_attendance(p_department_id, p_business_day_id, p_attendance_entries)`
- `get_business_day_cast_sales_adjustment_status(p_department_id, p_business_day_id)`
- `get_cast_sales_adjustment_slips(p_department_id, p_business_day_id)`
- `get_cast_sales_adjustment_detail(p_department_id, p_slip_id)`
- `save_cast_sales_adjustment(p_department_id, p_slip_id, p_adjustments, p_source_amount_type, p_split_mode)`
- `close_business_day(p_department_id, p_business_day_id, p_memo, p_pending_receipt_status)`

### 伝票

- `get_store_tables(p_department_id)`
- `get_store_casts(p_department_id)`
  - 指定店舗と同じ会社内の有効店舗に所属する有効キャストを返します。
  - ヘルプ対応のため、現在店舗所属キャストだけに限定しません。
- `get_business_day_slips(p_department_id, p_business_day_id)`
- `get_order_entry_slips(p_department_id, p_business_day_id)`
- `get_store_order_items(p_department_id)`
- `get_store_item_admin_catalog(p_department_id)`
- `upsert_store_item_category(p_department_id, p_item_category_id, p_category_code, p_category_name, p_sort_order, p_is_active)`
- `upsert_store_item(p_department_id, p_item_id, p_item_category_id, p_item_name, p_default_price, p_is_active, p_is_cast_back_target, p_cast_back_regular_unit_amount, p_cast_back_nomination_unit_amount, p_cast_back_type)`
- `delete_store_item(p_department_id, p_item_id)`
- `add_store_order_lines(p_department_id, p_slip_id, p_order_lines)`
- `create_store_slip(p_department_id, p_table_id, p_opened_at, p_customer_labels, p_cast_nominations, p_memo)`
  - `p_cast_nominations` は `cast_id`, `nomination_type`, `companion_time`, `nomination_price` を持つJSON配列です。
  - `nomination_price` は1000円から20000円まで1000円刻みで、会計額へ加算します。
- `save_store_slip_adjustments(p_department_id, p_slip_id, p_adjustment_lines)`
  - 通常商品とは別枠の自由入力明細を保存します。商品マスタへは登録しません。
  - `amount` は負値を許容し、会計合計額へ直接加減します。
- `save_store_karaoke_lines(p_department_id, p_business_day_id, p_karaoke_lines)`
  - カラオケ商品の注文行を伝票単位で一括保存します。
  - カラオケは `store_item_master.item_type = 'karaoke'`、1回200円固定、サービス料対象です。
  - 同一伝票内ではカラオケ注文行を1行に集約し、`ordered_at` は入店時刻に合わせます。数量0はアクティブ行を残しません。

### 領収書

- `get_pending_receipts(p_department_id, p_status)`
- `quick_enter_receipt(p_department_id, p_document_id, p_payment_date, p_amount, p_account_subject, p_description, p_group_code, p_journal_payload, p_status)`
- `mark_receipt_scan_mistake(p_department_id, p_document_id, p_status)`
  - 領収書の管理入力は、DocManagementの `save_journal_payload` 契約に従ったpayloadを作成し、ProsperAppの `quick_enter_receipt` RPCへ渡します。DocManagementアプリや `document-api` へ直接送信しません。
  - スキャンミス除外はDriveファイルを削除せず、DB上のステータス更新で入力対象から外します。

## 画面構成

### UI設計方針

- 店舗業務の大きな状態は `営業中 -> 締め作業` を基本にします。営業日は現在時刻の正午切替ルールで決め、最初の業務入力POSTで自動作成します。
- `マスタ設定` は営業フローのステップに含めず、ページ最上部のタブから開く任意管理領域として扱います。
- 未締めの前営業日が残っている場合は新しい営業日を作らず、営業中画面から締め作業へ誘導します。
- 営業中画面の締め未完了警告はコンパクトに表示し、締め作業への移動は上部タブに委ねます。
- 新規画面や既存画面の改修では、共通ヘッダー、共通状態パネル、共通ボタンルールを優先して使います。画面ごとに別のカード表現や状態色を増やさないでください。
- 上部タブが `営業中` / `締め作業` の大きな状態表示を担うため、各トップ画面には同じ役割のステータスバーを置かず、日付や未処理件数は見出しや操作パネル内の補足に留めます。
- 状態表示の語彙は `is-ready`、`needs-action`、`has-warning`、`is-preparing` を基本にします。緑は完了、橙は要確認/準備中、赤は要入力/ブロックを表します。
- ボタン階層は、画面の主要操作を `btn-primary`、戻る/営業中へ戻る/キャンセルを `btn-outline-secondary`、削除や営業日締めなどの危険操作を `danger` 系に固定します。
- 文言は、未処理、次操作、危険操作がすぐ判断できることを優先します。UI刷新ではなく、店舗スタッフが閉店前の見落としや操作ミスを避けられることを判断基準にします。

### 配置ルール

- 利用者にHTML画面として見せるものだけを `Pages/` に置きます。
- ファイル返却、API的な処理、画面を持たない処理は `Endpoints/` に置きます。
- 機能専用の部分ビューは、該当機能フォルダ配下に `_*.cshtml` として置きます。
- 本番例外表示の `/Error` は利用者向けHTML画面なので `Pages/Error.cshtml` に残します。

- `/`
  - 営業中画面。
  - 営業中に操作する画面として扱い、伝票一覧を最優先に配置します。
  - 一番上に営業日、未会計数、会計済数のコンパクトなサマリーを置きます。
  - 営業中の伝票起こしと当日伝票一覧を中心に置き、勤怠管理への導線はサマリー内に少し強調したコンパクトなボタンで置きます。
  - 営業中の営業日がない場合は「最初の入力で営業日を自動作成」と表示し、明示的な営業日開始ボタンは置きません。
  - 酒代、領収書、営業日締めなど勤怠以外の締め作業は `/Closing` に集約します。
  - 伝票一覧は大きな枠付きパネルにまとめず、スタッフが一覧からすぐ伝票へ入れる構成を優先します。
  - 伝票一覧の会計額は常に隠し、右下寄りの固定オーバーレイボタンに触れている間だけ表示します。
  - 営業中カラオケは伝票行の `+` / `-` で画面内ドラフトを即時更新し、最後の操作から短時間後に差分だけ自動保存します。手動保存ボタンは置かず、伝票詳細へ遷移する前にも未保存分の保存を短く試みます。失敗時は `localStorage` の未送信ドラフトを残します。

- `/Management`
  - 上部タブの `マスタ設定` 入口です。
  - キャスト情報と商品情報は、営業日前の必須作業ではなく、ここから任意のタイミングで開く導線にします。
  - 管理メニューも操作パネルの縦並びを維持します。

- `/Management/Casts`
  - マスタ設定タブから開くキャスト情報確認/編集導線用です。
  - 登録済みキャスト確認、キャスト作成、キャスト削除を扱います。

- `/Management/Items`
  - マスタ設定タブから開く商品管理です。
  - 非管理者はカテゴリを編集できません。
  - 商品名は店舗内uniqueです。
  - 商品コード、商品表示順など利用者が判断しづらい項目は画面に出しません。
  - 商品の並び替えが必要になったら専用UIで扱います。
  - 商品は既存行を直接編集せず、追加と削除で運用します。
  - カラオケは `item_type = 'karaoke'` のシステム商品として商品マスタに置きます。1回200円固定で、通常の商品小計に含めてサービス料対象にします。通常の商品削除RPCでは削除できません。
  - 注文履歴は `item_name_snapshot` / `unit_price` / `amount` を保持するため、商品マスタを再参照しません。
  - 商品削除は `delete_store_item` で商品マスタ行を削除し、既存注文行の `item_id` は切り離します。

- `/Slips/Edit`
  - 伝票詳細、客追加、指名追加、オーダー追加、会計処理。
  - 営業中画面の当日伝票一覧から遷移します。
  - 指名追加は本指名をデフォルトにし、指名種別の次に1000円から20000円までの指名価格を選択します。
  - 自由入力明細は通常商品とは別枠で伝票/会計に表示します。
  - カラオケは商品としてオーダー一覧に表示し、時刻列は入店時刻に固定します。異なるタイミングで追加したカラオケも同一伝票内では1行に集約し、数量のみを増減します。
  - 会計確定後は `ReceiptPrinter` 設定が有効な場合だけ、SII向け端末側ブリッジへ領収書印刷要求を非同期送信します。印刷失敗で会計確定は取り消しません。

- `/Orders`
  - オーダー入力。
  - open伝票を卓番として選択し、カテゴリ別の商品ボタンから注文キューへ追加します。
  - 同じ商品は数量加算し、一括登録後は卓番選択へ戻ります。

- `/Closing`
  - 締め作業画面。
  - 酒代入力、勤怠確認、キャスト売上額調整、領収書入力を縦並びの独立パネルで表示します。
  - 営業日締めは通常の作業パネルから分離し、締め条件と最終実行ボタンを下部にまとめます。
  - 酒代入力、勤怠確認、キャスト売上額調整、領収書入力は締め前の必須作業です。未完了の必須作業は赤、確認対象は橙、完了は緑で表示します。
  - キャスト売上額調整は `/Closing/CastSalesAdjustment` の専用ページで、会計済みかつ指名キャストがいる伝票を一覧表示し、客名とキャストごとの売上分配額を一覧上で確認できるようにします。売上額調整は行末のボタンから開くモーダルで保存します。
  - 領収書入力は未入力がある場合に要入力として表示し、営業日締めのブロック条件にします。
  - 営業日締めは、未会計伝票0、酒代入力済み、勤怠1名以上、退勤未入力0、キャスト売上額調整済み、領収書入力完了を満たした場合だけ実行できます。画面POSTと `close_business_day` RPCの両方で同じ条件を検証します。

- `/Closing/Attendance`
  - 出勤登録を統合した勤怠入力画面。
  - 出勤者選択、出勤時刻、退勤時刻、送り利用有無を同じ画面で入力します。
  - 退勤時刻は営業中の途中保存では空でも保存でき、締め前チェックで未入力警告を出します。
  - 退勤時刻の選択肢は端末設定の `AttendanceMinuteStep` に従います。

- `/Closing/Receipts`
  - 領収書簡易入力。
  - Google Driveプレビュー、DocManagement契約payload作成、Supabase RPC更新、スキャンミス除外、PDF先読みキャッシュを含みます。
  - 入力保存時はDocManagement契約の `journal_entries`、`journal_entry_lines`、`document_journal_links` を `quick_enter_receipt` RPC payloadに含め、領収書ステータスを完了へ更新します。
  - スキャンミス除外はDB上の論理削除として扱い、Driveファイルは削除しません。
  - Driveファイルの取得は画面ではなく `/DrivePreview/{driveFileId}` endpoint で行います。

- `/Login`
  - Google認証ログイン入口。
  - Google設定または許可アカウント設定が不足している場合は、OAuthへ進まず設定不足を表示します。

- `/Error`
  - 本番例外時のフォールバック画面。
  - `Program.cs` の `UseExceptionHandler("/Error")` から使います。
  - 通常導線には置きません。

- `/Settings`
  - 管理者設定。
  - パスワードは固定で `4245`。
  - 設定値は端末ローカル保存です。

## 非画面endpoint

- `/DrivePreview/{driveFileId}`
  - Google Driveファイルをアプリサーバー経由で返す認証付きプロキシです。
  - Razor Pageではなく `Endpoints/DrivePreviewEndpoints.cs` で定義します。
  - `prefetch=1` は入力作業を妨げないよう、失敗時も画面遷移を起こしません。

## Azure / 環境変数

Azure App Serviceでは最低限以下が必要です。

- `Supabase__Url`
- `SUPABASE_RPC_EDGE_FUNCTION_URL` または `Supabase__RpcEdgeFunctionUrl`
- `Supabase_Edge_Key` または `SUPABASE_RPC_EDGE_FUNCTION_KEY`

`SUPABASE_RPC_EDGE_FUNCTION_URL` 未設定時は `Supabase__Url` と `Supabase__RpcProxyFunctionName` から `/functions/v1/prosper-rpc` を組み立てます。
アプリ側のSupabase RPC呼び出しでは、Edge Functionキーを `x-prosper-rpc-api-key`、`apikey`、`Authorization: Bearer` に設定します。

`prosper-rpc` Edge Function側では以下が必要です。

- `SUPABASE_DB_URL`
- `ProsperApp_API_KEY` または `PROSPER_RPC_API_KEY`

Google Drive OAuth/プレビューを使う場合は以下も必要です。

- `GoogleDrive__ClientId`
- `GoogleDrive__ClientSecret`
- `GoogleDrive__Scopes__0` など
- `GoogleAuth__AllowedEmails__0` または `GoogleAuth__AllowedDomains__0` など

## GitHub Actions / Azure自動デプロイ

リポジトリルートの `.github/workflows/azure-app-service.yml` は `main` へのpush、または手動実行でAzure App Serviceへデプロイします。

GitHub側に以下を設定してください。

- Repository variable `AZURE_WEBAPP_NAME`
  - Azure App Serviceのアプリ名です。
- Repository secret `AZURE_WEBAPP_PUBLISH_PROFILE`
  - Azure PortalのApp Serviceから取得したPublish profile XML全文です。

ワークフローは .NET `10.0.x` をセットアップし、`dotnet restore`、`dotnet build -c Release`、`dotnet publish -c Release /p:UseAppHost=false` の後、`azure/webapps-deploy@v3` で発行します。

Azure App Service側のアプリ設定には、上記の `Supabase__...`、`GoogleDrive__...`、`GoogleAuth__...` などを設定してください。秘密情報やPublish profileはリポジトリへコミットしません。

## 注意点

- `AGENTS.md` は現在PowerShell表示上で文字化けして見える場合があります。UTF-8前提で扱ってください。
- `Sql/quick_entry_account_master_updates.sql` には実際に文字化けした日本語が残っている可能性があります。実行前に修正してください。
- サブエージェントには単純な調査・軽量レビュー・低リスク編集だけを委任します。
- SQL/RPC設計、会計、給与、認証、RLS、Google Drive権限まわりの判断はメインCodexが行います。
- このフォルダは現時点でgitリポジトリとして認識されない可能性があります。`git diff` 前提で作業しないでください。

## 次に着手しやすい作業

1. Supabaseで `Sql/store_rpc/*.sql` が順番どおり実行済みか確認する。
2. `quick_entry_account_master_updates.sql` の文字化けを修正する。
3. 営業中画面を「伝票起こしボタン + 当日伝票一覧 + 詳細/会計ボタン」に寄せる。
4. 締め作業をステップ式に整理する。
5. 伝票編集、会計処理、注文入力のRPCと画面を追加する。
