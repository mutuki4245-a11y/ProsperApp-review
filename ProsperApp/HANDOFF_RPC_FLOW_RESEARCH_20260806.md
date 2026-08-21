# ProsperApp RPC フロー調査ノート

調査日: 2026-08-06
対象: リポジトリに存在する Razor Pages、PageModel、クライアント JavaScript、アプリケーションサービス、Supabase リポジトリ、Edge Function 許可リスト、`Sql/` のRPC定義。
目的: 主要フローで「どのUI操作が、どのRPC・データ・キャッシュを何回通るか」を、実装ソースから棚卸しする。ここでは設計採否や変更方針は決めない。

## 読み方と前提

- 回数は **ブラウザ/APIサーバーから Supabase Edge Function への HTTP RPC** 回数である。`0/1` はアプリ内キャッシュの hit/miss による差、`N` は入力件数・操作件数に依存する。
- 実際のSQL実行数、実行計画、ネットワーク遅延、キャッシュhit率は計測していない。SQL内で別の`store.*`関数を呼ぶものは「SQL内の間接処理」として明記する。
- アプリのSupabase DBアクセスは、確認した範囲では PostgREST のテーブルRESTではなく、`ISupabaseRpcClient` が Edge Functionへ POST する方式のみである。HTTP送信箇所は `Infrastructure/Supabase/SupabaseRpcClient.cs:74-102`、Edge Function側の動的な `select * from schema.function(...)` は `supabase/functions/prosper-rpc/index.ts:634-669`。
- 以下の「テーブル」はSQLソースに書かれた参照/更新先。現在デプロイ済みのSQL・Edge Functionがこのリポジトリと一致することは本調査では検証していない。

## 共通の通信・キャッシュ基盤

### Edge Functionの許可リスト

`prosper-rpc` は `rpcDefinitions` に列挙された`store.*`だけを受け付け、未知の名前は400 `invalid_function_name` で拒否する（`supabase/functions/prosper-rpc/index.ts:42-602, 634-645`）。本アプリが呼ぶ全RPCはこの許可リストに存在する。したがって、SQLに関数定義があるだけではアプリからは呼べず、Edge Functionの許可リストとの整合も必要である。

### アプリ内メモリキャッシュ

`ApplicationMemoryCache` はDIで Singleton 登録されるプロセス内`IMemoryCache`ラッパーであり、分散・ブラウザ共有ではない（`Program.cs:29-33`, `Infrastructure/Caching/ApplicationMemoryCache.cs:6-54`）。キーの状態を管理画面に出せるが、`Remove`しても登録簿からは消えず、`ClearAll`は登録済み全キーを削除する（同:52-86）。

| 区分 | キー/内容 | TTL | 根拠 |
|---|---|---:|---|
| マスタ | departments, context, tables, casts, staffs, order-items, item catalog, nomination, payment methods, pricing plan, bootstrap payload | なし（プロセス再起動/明示削除まで） | `StoreMasterCacheKeys.cs:9-47` |
| 稼働中 | current business day, order attending casts | 30秒 | `StoreMasterCacheKeys.cs:7, 35-37, 50-57` |
| 領収書待ち一覧 | `receipt-pending:{department}:{status}` | 30秒 | `SupabaseReceiptRepository.cs:17, 30-57, 169-175` |
| Google Drive本文 | `drive-preview:{fileId}` | 10分 | `GoogleDriveFileService.cs:17, 43-46, 81-106` |
| ブラウザlocalStorage | 営業中編集ドラフト、会計伝票印刷キュー、注文キュー | 明示削除まで（サーバーキャッシュではない） | `business-home.js:48-51`, `business-checkout.js:80-97`, `order-entry.js:1-30` |

`store.get_store_bootstrap` は一度のRPCで context、営業日、各マスタ、出勤キャスト、営業中snapshotを返し、受信後に上のキャッシュ群を水和する（`SupabaseStoreMasterBootstrapper.cs:55-82, 85-333`、返却フィールドは `Sql/store_rpc/15_business_home_bootstrap.sql:4-21`）。`EnsureAsync` は bootstrap payload cache を二重チェックし、部署ごとの `SemaphoreSlim` で同時missを一本化する（`SupabaseStoreMasterBootstrapper.cs:24-52`）。一方、営業中トップ用の `GetBusinessHomeBootstrapAsync` は `EnsureAsync` ではなく `GetStoreBootstrapAsync` を呼ぶため、トップのサーバーGETでは bootstrap payloadが既にあっても RPC を再送する（`SupabaseStoreSlipRepository.cs:125-134`）。

主要な明示削除は、マスタ更新後に該当マスタと bootstrap payloadを削除すること（`StoreMasterCacheKeys.cs:59-100`）、営業日開始/終了・勤怠更新後に current day / attending casts を削除すること（`SupabaseBusinessDayRepository.cs:181-188, 227-229, 339-340, 576-577`）である。

## SQL内の基礎データ対応

### 高頻度・集約読み取り

| RPC | SQL定義・直接/間接データ |
|---|---|
| `store.get_store_bootstrap` | `Sql/store_rpc/15_business_home_bootstrap.sql:4-153`。`get_context`, `get_current_business_day`, departments, tables/table admin, casts/cast admin, staffs/staff admin, order items/item catalog, nomination, payment methods, pricing planを呼び、営業日があれば attending casts と snapshotも読む（同:52-132）。下位SQLから `department_master`, `store_business_days`, `store_table_master`, `cast_master`, `store_staff_master`, `store_item_category_master`, `store_item_master`, `store_nomination_back_master`, `payment_method_master`, `store_pricing_plan_master`, `store_cast_attendance`, `store_slips`, `store_slip_customers`, `store_slip_casts`, `store_order_lines`, `store_order_line_cast_backs`, `store_slip_charge_lines`, `store_slip_pricing_lines`, `store_slip_accounting_snapshots` を参照する。 |
| `store.get_business_day_snapshot` | `Sql/store_rpc/09_business_home_snapshot.sql:451-469` は `get_business_day_snapshot_at` を呼ぶ。同:7-448 は営業日、伝票、顧客、伝票キャスト、キャスト/部署、注文・商品・注文バック、調整、料金行、会計snapshot、卓を集約し JSON snapshotにする。 |
| `store.get_business_day_closing_readiness` | `Sql/store_rpc/01_business_day.sql:969-1147`。営業日・キャスト/スタッフ勤怠を直接参照し、`get_business_day_cast_sales_adjustment_status`, `get_business_day_champagne_back_status`, `get_pending_receipts` をSQL内から呼ぶ。1 HTTP RPCだが締め状態・領収書待ち・2種類の締め入力をまとめて確認する。 |
| `store.get_business_day_daily_report` | `Sql/store_rpc/12_daily_report.sql:375-617`。closed snapshotがあれば `store_business_day_closing_snapshots` を、なければ `build_business_day_daily_report`（同:5-372）を呼ぶ。後者は営業日、伝票・会計・決済・注文・各種バック・勤怠・会計仕訳/証憑を集計する。 |

### 主な書込みRPCとSQL上の対象

| RPC | 主対象テーブル（SQL定義） |
|---|---|
| `open_business_day` / `_with_attendance` | `store_business_days`; 後者は `store_cast_attendance`, `cast_master`, `department_master` も扱う（`01_business_day.sql:74, 148`）。 |
| `save_business_day_attendance` / `save_business_day_staff_attendance` | `store_business_days`, `store_cast_attendance`, `cast_master` / `store_staff_attendance`, `store_staff_master`（同:274, 415）。 |
| `save_business_day_closing_attendance` / staff版 | `store_business_days`, `store_cast_attendance` / `store_staff_attendance`（同:669, 782）。 |
| `save_business_day_drink_delivery_amount` / `close_business_day` | `store_business_days`; closeは readiness SQLも呼ぶ（同:932, 1149）。 |
| `create_slip` | `store_slips`, `store_slip_customers`, `store_slip_casts`, `store_slip_cast_backs`, `store_order_lines`; validation/lookupとして business day, table, cast, nomination master, item, department（`05_checkout.sql:149-424`）。 |
| `flush_business_home_changes` | `store_business_home_flush_batches`, `store_slips`、各編集操作が顧客/キャスト/注文/調整の各表を更新し、karaokeが `store_order_lines`, `store_slip_charge_lines`, `store_slip_customers` 等を扱う（`10_business_home_flush.sql:74-240`, `09_business_home_snapshot.sql:473-645`, `03_slips.sql:7-974`）。 |
| `add_order_lines` | `store_order_lines`, `store_order_line_cast_backs`; 対象伝票、商品、出勤、伝票キャストを検証（`04_orders.sql:41-193`）。 |
| 会計系 | `issue_checkout_statement` は pricing/order/customer/slipを更新し accounting snapshotを作成、`confirm_checkout` は `store_checkouts`, `store_checkout_payments`, `store_slip_accounting_snapshots`, slip を扱う、`cancel_checkout` は上記の状態を戻す（`08_checkout_ready.sql:426-584, 741-949`, `05_checkout.sql:10-146`）。 |
| 領収書入力 | `accounting.documents`, `accounting.document_journal_links`, `accounting.journal_entries`, `accounting.journal_entry_lines`, `accounting.upload_source_master`。キャスト前渡なら `store_business_day_cast_advances`, business day, cast attendance/castも扱う（`06_receipts.sql:111-325`）。 |

## 画面遷移・API操作の棚卸し

### 認証・共通画面

| UI起点 | HTTP/RPC | 回数・キャッシュ | データ/補足 | 根拠 |
|---|---|---|---|---|
| `/Login` GET、Googleログイン | Supabase RPCなし。Cookie/Google OAuth。必要時にGoogle access tokenをsessionから消去。 | DB RPC 0 | 外部Google認証フローであり、Drive token有無はsessionを読む。 | `Pages/Login.cshtml.cs:23-61`, `Program.cs:106-182` |
| `/logout` | Supabase RPCなし。session clear + cookie sign-out。 | 0 | — | `Program.cs:211-217` |
| `/Error` | Supabase RPCなし。 | 0 | trace idのみ。 | `Pages/Error.cshtml.cs:8-20` |
| レイアウトのナビゲーション | リンク先の各Razor GET。ナビ自体にはfetchなし。 | 遷移先に依存 | 営業中/締め/店舗設定/管理者設定へのリンク。 | `Pages/Shared/_Layout.cshtml:49-89` |

### 営業中トップ `/`（`IndexModel`）

| UI起点/操作 | PageModel → サービス → RPC | HTTP RPC回数 | 取得/更新データとキャッシュ・再読込 |
|---|---|---:|---|
| 初回GET（sales-management画面） | `LoadPageAsync` → `GetBusinessHomeBootstrapAsync` → `store.get_store_bootstrap` | 1 | context、営業日、卓、指名、商品、出勤キャスト、決済、snapshotを一括取得し、マスタと30秒runtime cacheを水和。トップでは bootstrap cacheを読まず常に fetchする。`IndexModel` は初期snapshotをHTML/JSへ埋め込む。`Pages/Index.cshtml.cs:138-148,425-448`; `BusinessHomeApplicationService.cs:46-110`; `SupabaseStoreSlipRepository.cs:125-203`。 |
| `ScreenMode=order-entry` で `/` を開く | `OnGetAsync` が先に `/Orders/Index` へredirect。 | 0 | Bootstrap前に戻る。 | `Pages/Index.cshtml.cs:138-145` |
| 営業中一覧の再取得（初期snapshot不正時、10秒ごと、focus/visible/online、会計後） | `OnGetBusinessSlips` → `GetSnapshotAsync` → current day → `store.get_business_day_snapshot` | 1〜2 | current day runtime cache hitなら snapshotの1回のみ、30秒missなら `get_current_business_day` + snapshot。営業日なしは current確認のみ。10秒ポーリングは `business-home.js:52,1557-1585,1981-2022`、サービス連鎖は `BusinessHomeApplicationService.cs:19-44`。 |
| 伝票作成modalでキャスト候補を開く | `OnGetAttendanceCasts` → current day → `get_order_attending_casts` | 0〜2 | Indexのbootstrapが直前にruntime cacheを入れるため通常0。current/attendingのTTL後は最大2。modal内では一度成功するとブラウザ内 `castOptionsLoaded` も使う。`Pages/Index.cshtml.cs:151-177`; `BusinessHomeApplicationService.cs:112-126`; `create-slip-modal.js:64-90`。 |
| 伝票作成POST 成功 | 事前 `LoadAsync` → createの `EnsureCurrentAsync` → `store.create_slip` → 成功後 `LoadAsync` | 既存営業日: 3〜4。営業日なし: 5 | 事前/事後の各bootstrap=2。Ensureはruntime hitなら0、missならcurrent=1、営業日なしはcurrent=1+open=1。createは1。POSTが`Page()`で終わるため、成功時もPRGではなくサーバー内再ロード。 | `Pages/Index.cshtml.cs:254-285`; `SupabaseStoreSlipRepository.cs:294-345`; `SupabaseBusinessDayRepository.cs:75-128` |
| 営業中編集（顧客、指名、調整、注文、カラオケ） | `OnPostFlushBusinessHomeChanges` → current day → `store.flush_business_home_changes` | 1〜2 / 送信バッチ | クライアントは操作をMapに溜め、0ms timerで一括POSTし、返却snapshotをそのまま表示する。current day cache hitなら1。操作種別は9種類（顧客3、指名2、調整2、注文2）。 | `Pages/Index.cshtml.cs:216-251`; `BusinessHomeApplicationService.cs:128-170`; `BusinessHomeOperationContract.cs:49-60`; `business-home.js:1678-1715,1739-1765` |
| 同フラッシュ内のSQL処理 | 上と同じ単一HTTP RPC | HTTPは1、SQL内snapshot構築は操作数`N`+最終1 | `flush_business_home_changes` は各operationに `apply_business_slip_editor_operation` を呼び、その関数自身が毎回 `get_business_day_snapshot` を実行して返す。flush側も最後にsnapshotをもう一度構築する。返された各operationのsnapshotは`perform`で捨てられる。 | `Sql/store_rpc/10_business_home_flush.sql:117-152, 236-240`; `Sql/store_rpc/09_business_home_snapshot.sql:637-644` |
| 未保存編集がある状態で画面遷移/フォーム送信 | 先に上記flush、成功後に通常フォーム/リンクへ遷移。 | +1〜2 | flush失敗なら遷移を止める。 | `business-home.js:1776-1798, 1941-1960` |
| 会計伝票発行 | `OnPostIssueCheckoutStatement` → `store.issue_checkout_statement` | 1 | 料金行・会計準備・statement/review print dataをRPC結果で返す。 | `Pages/Index.cshtml.cs:289-309`; `SupabaseCheckoutRepository.cs:55-78`; `business-checkout.js:398-418` |
| 会計伝票印刷データ復旧 | `OnPostGetCheckoutStatementPrintData` → `store.get_checkout_statement_print_data` | 1 | accounting snapshot + slip読取。`checkout_ready`の復旧時に使う。 | `Pages/Index.cshtml.cs:311-330`; `SupabaseCheckoutRepository.cs:80-92`; `business-checkout.js:388-396` |
| 会計準備解除 | `OnPostReleaseCheckoutReady` → `store.release_checkout_ready`、成功後一覧refresh | 1 + 1〜2 | 解除RPC後に `prosper:business-slips-refresh` が一覧snapshotを取り直す。 | `Pages/Index.cshtml.cs:332-351`; `business-checkout.js:461-471,572` |
| 会計確定 | `OnPostConfirmCheckout` → `store.confirm_checkout`、成功後一覧refresh | 1 + 1〜2 | confirm結果に領収書print dataを含む。別の領収書再印刷要求は発生しない限り不要。 | `Pages/Index.cshtml.cs:353-381`; `SupabaseCheckoutRepository.cs:110-149`; `business-checkout.js:491-510,572` |
| 領収書再印刷 | `OnPostGetCheckoutReceiptPrintData` → `store.get_checkout_receipt_print_data` | 1 | `store_checkouts` / slipからprint dataを読む。 | `Pages/Index.cshtml.cs:383-402`; `SupabaseCheckoutRepository.cs:151-172`; `business-checkout.js:518-523` |
| 会計取消 | `OnPostCancelCheckout` → `store.cancel_checkout`、成功後一覧refresh | 1 + 1〜2 | 会計/決済/料金等を戻した後に一覧snapshotを再取得。 | `Pages/Index.cshtml.cs:404-423`; `SupabaseCheckoutRepository.cs:174-189`; `business-checkout.js:525-531,572` |

### 注文画面 `/Orders/Index`

| UI起点/操作 | RPC | HTTP RPC回数 | 取得/更新データとキャッシュ・再読込 |
|---|---|---:|---|
| 初回GET | `EnsureAsync`（coldならbootstrap）を先に実行後、context/current/itemsを並列、営業日があれば attending castsを順次取得。 | warm: 0、cold: 1。cache期限/欠損なら最大3相当 | Bootstrapが context/current/items/attendingを水和するため通常は0。`LoadPageAsync` は attendanceを business day取得完了後に呼ぶ。 | `Pages/Orders/Index.cshtml.cs:50-60,137-147`; `OrderEntryApplicationService.cs:22-54`; 各cache取得は `SupabaseStoreSlipRepository.cs:23-123`, `SupabaseStoreOrderRepository.cs:61-157`。 |
| 伝票候補JSON（初回、10秒、focus/visible、手動更新） | current day → `store.get_order_entry_slips` | 1〜2 | current runtime hitなら1、missなら2。`get_order_entry_slips` は open伝票、卓、顧客、指名キャストを集約。 | `Pages/Orders/Index.cshtml.cs:63-98`; `OrderEntryApplicationService.cs:57-70`; `SupabaseStoreOrderRepository.cs:19-59`; `order-entry.js:313-340,790-808`。 |
| 注文登録POST | `LoadOptionsAsync` の上記初期読み + `store.add_order_lines` | warm: 1、cold: 2〜4 | POST前に商品/出勤等を再読込して検証してから1回の注文書込み。成功時はredirectせず`Page()`を返すため、表示一覧はこのPOSTでは更新されず、次の10秒/focus/手動候補更新で読み直す。 | `Pages/Orders/Index.cshtml.cs:100-130`; `OrderEntryApplicationService.cs:83-86`; `SupabaseStoreOrderRepository.cs:160-204`。 |

### 勤怠 `/Attendance` および `/Closing/Attendance`

| UI起点/操作 | RPC | HTTP RPC回数 | 取得/更新データとキャッシュ・再読込 |
|---|---|---:|---|
| GET | `EnsureAsync`、context/current/casts/staffsを並列、営業日ありなら closing attendanceを続けて取得。 | warmかつ営業日なし: 0; warmかつ営業日あり: 1; cold: +1 | bootstrapが最初の4種を満たすが、closing attendanceは非キャッシュ。 | `Pages/Attendance.cshtml.cs:46-56,103-126`; `AttendanceApplicationService.cs:20-81` |
| 保存POST | POST冒頭の上記Load → force current → 必要なら open → cast attendance保存 → staff attendance保存 → closing attendance読取 → cast closing保存 → staff closing保存 → redirect先GET | 最少（出退勤更新なし）でも前段load+force current+closing attendance+redirect。入力に応じ書込みは0〜4、営業日自動作成時はさらに2 | キャスト・スタッフ・各退勤保存は別RPCで、全員分は各RPCのJSON配列でまとめられる。保存後の closing attendance読取は attendance ID解決のため常に行われる。成功後は営業中なら同一URL、締め導線ならClosing Indexへredirect。 | `Pages/Attendance.cshtml.cs:58-96`; `AttendanceApplicationService.cs:100-260`; RPC実体 `SupabaseBusinessDayRepository.cs:296-386,495-628` |

### 締めトップ `/Closing/Index` と日報

| UI起点/操作 | RPC | HTTP RPC回数 | 取得/更新データとキャッシュ・再読込 |
|---|---|---:|---|
| 初回GET | current business day | 0〜1 | `includeReadiness:false`のため営業日だけ。runtime 30秒hitなら0。 | `Pages/Closing/Index.cshtml.cs:48-76`; `ClosingApplicationService.cs:13-49` |
| 締めパネル状態（初回、30秒、focus/visible、再取得） | readiness endpoint: force current + `get_business_day_closing_readiness`; receipts endpoint: `get_pending_receipts` | 2 + 0〜1、並列 | JavaScriptは2エンドポイントを`Promise.all`する。readinessは強制更新のため常に2（営業日なしならcurrentのみ1）。領収書待ちは30秒cache。 | `Pages/Closing/Index.cshtml.cs:79-158`; `Pages/Closing/Index.cshtml:634-774,780-797`; `ClosingApplicationService.cs:13-49` |
| 日報（画面表示時、手動、provisional中30秒、focus/visible） | `store.get_business_day_daily_report` | 1/回 | no-store fetch、アプリ内cacheなし。closing後は `ReportBusinessDayId` を使う。 | `Pages/Closing/Index.cshtml.cs:160-190`; `daily-report.js:1-16,343-401`; `SupabaseDailyReportRepository.cs:20` |
| 営業日締めPOST | force current → readiness（通常）→ `close_business_day` → redirect | 3（overrideなら2）+ 遷移先のGET/パネル再取得 | failed時は readiness付きreloadをさらに2回行う。成功後のredirect先では current GETと、JSのreadiness/receipts、日報自動ロードが加わる。 | `Pages/Closing/Index.cshtml.cs:192-245`; `ClosingApplicationService.cs:51-111`; `SupabaseBusinessDayRepository.cs:191-230` |

### 締め個別画面

| 画面/操作 | RPC回数と処理 | データ・キャッシュ |
|---|---|---|
| 納品額 `/Closing/DrinkCost` GET | current 0〜1、営業日ありなら `get_business_day_drink_delivery_status` 1。 | currentは30秒cache、statusは非キャッシュ。`Pages/Closing/DrinkCost.cshtml.cs:30-40,100-148`。 |
| 納品額保存 | POST冒頭に上記Load（1〜2）、営業日なしならEnsure（current+open最大2）、`save_business_day_drink_delivery_amount` 1、Closingへredirect。 | 対象は business dayの納品額。`Pages/Closing/DrinkCost.cshtml.cs:42-98`; `SupabaseBusinessDayRepository.cs:459-493`。 |
| キャスト売上調整 GET | `EnsureAsync` cold時bootstrap1、context/currentを並列（通常cache）、営業日ありなら `get_business_day_cast_sales_adjustment_overview` 1。 | overviewは status/slips/detailsを一RPCで返し、個別detail RPC群をページ側は呼ばない。`Pages/Closing/CastSalesAdjustment.cshtml.cs:55-63,168-237`; `SupabaseCastSalesAdjustmentRepository.cs:12-...`。 |
| キャスト売上個別保存 | 事前Load + `save_cast_sales_adjustment` 1 + 成功後Load。 | 保存後Loadでoverviewを再取得。`Pages/Closing/CastSalesAdjustment.cshtml.cs:66-107`; `SupabaseCastSalesAdjustmentRepository.cs:102-...`。 |
| キャスト売上一括確認 | 事前Load + `save_business_day_cast_sales_adjustments` 1 + Closingへredirect。 | 入力済detailsを1 JSON配列で保存する。`Pages/Closing/CastSalesAdjustment.cshtml.cs:109-166`; Edge allowlist `prosper-rpc/index.ts:581-589`。 |
| シャンパンバック GET | current 0〜1、営業日ありなら `get_business_day_champagne_back_overview` 1。 | overviewはstatus/castsを一括返却、アプリcacheなし。`Pages/Closing/ChampagneBacks.cshtml.cs:32-41,94-155`; `SupabaseChampagneBackRepository.cs:12-74`。 |
| シャンパンバック保存 | 事前Load + `save_business_day_champagne_backs` 1 + Closingへredirect。失敗時はさらにLoad。 | 全キャストのbackを1 JSON配列で保存。`Pages/Closing/ChampagneBacks.cshtml.cs:44-86`; `SupabaseChampagneBackRepository.cs:76-120`。 |
| 領収書 `/Closing/Receipts` GET | force current 1 + 営業日ありなら closing attendance 1 + pending receipts 0〜1。 | pendingは30秒cache、currentはforceなので必ずRPC。`Pages/Closing/Receipts.cshtml.cs:72-86,196-272`; `SupabaseReceiptRepository.cs:21-73`。 |
| 領収書の次へ保存 | 事前 `LoadCurrent`（上記2〜3）→ context cache miss時 `get_context` 1 → `quick_enter_receipt` 1 → pending cache削除 → `GetPending` 1 → redirect先GET（上記2〜3）。 | contextがbootstrap済みなら`get_context`は0。pending cacheは書込み成功時に削除される。`Pages/Closing/Receipts.cshtml.cs:89-121,178-234`; `SupabaseReceiptRepository.cs:75-130,194-225`。 |
| 領収書skip | `get_pending_receipts` 0〜1 → 次indexへredirectなら次GET（2〜3）。 | skip自体はDB更新なし。`Pages/Closing/Receipts.cshtml.cs:123-143`。 |
| スキャンミス | `mark_receipt_scan_mistake` 1 → pending cache削除 → `GetPending` 1 → redirect先GET（2〜3）。 | Drive本文cacheは対象file IDだけ削除。`Pages/Closing/Receipts.cshtml.cs:145-175`; `SupabaseReceiptRepository.cs:133-175`。 |
| Driveプレビューと次件prefetch | 各 `/DrivePreview/{id}` は pending許可確認（pending cache 0〜1）後、Drive本文cache hitなら外部Google 0、missなら metadata GET + media GET の2。 | 現在表示のiframeと次件の `prefetch=1` が別々に実行されうる。DB RPCではなくGoogle Drive REST。`Endpoints/DrivePreviewEndpoints.cs:16-58`; `GoogleDriveFileService.cs:19-95,116-138`; `Pages/Closing/Receipts.cshtml:274-282`。 |

### 店舗設定・マスタ管理

| 画面/操作 | RPC回数と処理 | キャッシュ・再読込 |
|---|---|---|
| `/Management/Index` GET / 画面表示設定保存 | 0 | local cookieとsessionのみ。cache status表示、Clear Cacheは全`ApplicationMemoryCache`を削除して同ページへredirectする。`Pages/Management/Index.cshtml.cs:35-105`。 |
| `/Settings/Index` GET/unlock/lock/save | departments cache hit 0、missなら current departmentありでbootstrap 1、未選択なら `get_departments` 1。saveはcookie/session管理でDB RPCなし。 | 部署一覧は無期限master cache。`Pages/Settings/Index.cshtml.cs:53-145,196-203`; `SupabaseStoreSettingsRepository.cs:20-68,100-115`。 |
| Settings: non-master削除 | `delete_non_master_records` 1。 | current business day cacheだけを削除する。SQLはclosing/accounting/flush/pricing/payment/cast sales/champagne/order/slip/attendance/business-day等を削除する（`Sql/store_settings_functions.sql:75-249`）。`Pages/Settings/Index.cshtml.cs:147-193`; `SupabaseStoreSettingsRepository.cs:70-97`。 |
| 卓管理 GET | table-admin cache hit 0、cold bootstrap 1。 | `GetTablesAsync` はbootstrapから一覧を作る。`Pages/Management/Tables.cshtml.cs:29-39,99-108`; `SupabaseStoreTableAdminRepository.cs:17-55`。 |
| 卓save/delete | 書込み `upsert_table` / `delete_table` 1、成功後 table/boot cache削除→同リクエスト内Loadで bootstrap 1。 | invalid/failure時のLoadは削除前cacheを返し得る。`Pages/Management/Tables.cshtml.cs:41-96`; `SupabaseStoreTableAdminRepository.cs:58-124`; SQL `02_store_masters.sql:58,147`。 |
| キャスト管理 GET | bootstrap cold 1、current/cast adminは水和済みなら0。 | PageModelは `EnsureAsync`後に current と cast adminを並列化。`Pages/Management/Casts.cshtml.cs:40-49,150-176`。 |
| キャストcreate/delete/drink memo | 事前Load（通常0）+ 各書込み1 + cache削除 + 成功後Load（bootstrap1）。 | drink memoは attending casts runtime cacheも削除する。`Pages/Management/Casts.cshtml.cs:51-147`; `SupabaseStoreCastAdminRepository.cs:54-155`。 |
| スタッフ管理 GET / create/delete/employment | GETはキャスト管理と同型。各変更は事前Load+書込み1+staff/boot cache削除+成功後Load（通常1）。 | create/update/delete はそれぞれ `store.create_staff`, `update_staff_employment_type`, `delete_staff`。`Pages/Management/Staffs.cshtml.cs:42-178`; `SupabaseStoreStaffAdminRepository.cs:92-195`。 |
| 商品管理 GET | item catalog cache hit 0、cold bootstrap1。 | `Pages/Management/Items.cshtml.cs:42-52,217-226`; `SupabaseStoreItemAdminRepository.cs:18-79`。 |
| 商品カテゴリ/商品 save/delete/reorder | 各 `upsert_item_category`, `upsert_item`, `delete_item`, `delete_item_category`, `reorder_items` が1。成功後 items/boot cache削除→Loadのbootstrap1。 | SaveCategory/DeleteCategoryはadmin mode未満なら書込みなしだがcatalogをLoadする。`Pages/Management/Items.cshtml.cs:54-215`; `SupabaseStoreItemAdminRepository.cs:82-266`。 |
| 指名バック GET/save | GETはcache hit0/cold bootstrap1。saveは `save_nomination_back_master` 1→nomination/boot cache削除→Load bootstrap1。 | `Pages/Management/NominationBacks.cshtml.cs:23-92`; `SupabaseNominationBackAdminRepository.cs:18-106`。 |
| 料金設定 GET/save | GETはcache hit0/cold bootstrap1。saveは `save_pricing_plan_v2` 1→pricing/boot cache削除→Load bootstrap1。 | `Pages/Management/Pricing.cshtml.cs:23-74`; `SupabaseStorePricingPlanRepository.cs:18-102`。 |

## 全アプリRPCの実装対応一覧

この表は、上記画面表に現れるRPCをリポジトリ呼出し、SQL定義、主要表へ縮約した索引である。

| RPC | C#呼出し | SQL定義 | 主な表 |
|---|---|---|---|
| `get_store_bootstrap` | `SupabaseStoreMasterBootstrapper.cs:62-65` | `15_business_home_bootstrap.sql:4` | 上記「高頻度・集約読み取り」参照 |
| `get_current_business_day` | `SupabaseBusinessDayRepository.cs:45-72` | `01_business_day.sql:43` | `store_business_days` |
| `get_business_day_snapshot` | `SupabaseStoreSlipRepository.cs:207-230` | `09_business_home_snapshot.sql:451` | business day/slips/customers/casts/orders/charges/pricing/snapshots/master |
| `get_order_attending_casts` | `SupabaseStoreOrderRepository.cs:104-157` | `04_orders.sql:3` | cast attendance, cast, department |
| `get_order_entry_slips` | `SupabaseStoreOrderRepository.cs:19-59` | `02_store_masters.sql:627` | slips, table, customers, slip casts, casts |
| `add_order_lines` | `SupabaseStoreOrderRepository.cs:160-204` | `04_orders.sql:41` | order lines, order-line backs, slips/items/attendance/casts |
| `create_slip` | `SupabaseStoreSlipRepository.cs:294-345` | `05_checkout.sql:149` | slips/customers/casts/backs/order lines/master |
| `flush_business_home_changes` | `SupabaseStoreSlipRepository.cs:234-290` | `10_business_home_flush.sql:22` | flush batches + operation-dependent slip data |
| checkout six RPCs | `SupabaseCheckoutRepository.cs:55-189` | `08_checkout_ready.sql:426,587,615,741,951`; cancel `05_checkout.sql:10` | slips, orders/pricing/customers, checkouts/payments/accounting snapshots |
| attendance/open/close/drink RPCs | `SupabaseBusinessDayRepository.cs:75-628` | `01_business_day.sql:74-1149` | business days, cast/staff attendance |
| readiness | `SupabaseBusinessDayRepository.cs:232-294` | `01_business_day.sql:969` | business days/attendance + nested closing reads |
| cast sales overview/save/batch | `SupabaseCastSalesAdjustmentRepository.cs:12-187` | `14_operational_read_models.sql:277,330`; save detail `07_cast_sales_adjustments.sql:496` | checkouts/slips/slip casts/sales adjustments and related details |
| champagne overview/save | `SupabaseChampagneBackRepository.cs:12-120` | `14_operational_read_models.sql:42,104` | business days/cast attendance/champagne backs/cast/department |
| pending/quick entry/scan mistake | `SupabaseReceiptRepository.cs:21-166` | `06_receipts.sql:7,111,327` | accounting document/journal tables; advance-related store tables |
| daily report | `SupabaseDailyReportRepository.cs:20` | `12_daily_report.sql:375` | closing snapshot or current full operating/accounting data |
| masters | table/cast/staff/item/nomination/pricing repositories | `02_store_masters.sql`, `11_pricing.sql` | each corresponding master and `department_master` |
| departments/debug delete | `SupabaseStoreSettingsRepository.cs:20-115` | `store_settings_functions.sql:9,34` | `department_master`; destructive set listed above |

## 事実として確認できる重複・往復特性

以下は設計提案ではなく、ソースから数えられる反復・結合点である。

1. 営業中トップの初期GETは、キャッシュを水和する一括bootstrapを常に1回呼び、成功時は同じpayloadのsnapshotを初期表示に使う。その後、一覧を10秒で再取得する場合は current day + snapshot の2段階（current cache hitならsnapshotのみ）になる。
2. 締めトップはサーバーGETのcurrent取得とは別に、クライアントが30秒ごとに readiness（force current+readiness）とpending receiptsを並列取得する。日報も別に初期・30秒/focusで取得する。
3. 勤怠保存はキャスト出勤、スタッフ出勤、保存後attendance再読取、キャスト退勤、スタッフ退勤を別RPCにする。ただし各区分の全行はJSON配列で一括送信される。
4. 営業中編集はブラウザからは一括flush 1回だが、SQL実装は1 batch中の各editor operationごとにsnapshotを生成し、最後にも生成する。
5. マスタ更新系は成功直後に該当cacheとbootstrap payloadを削除し、ほとんどのPageModelが同一リクエストで一覧をLoadする。このため、成功操作は書込みRPCの直後にbootstrap RPCを追加で発生させる。
6. 領収書保存/スキャンミスはpending cacheを削除した後、次の対象を決めるため即座にpendingを再取得し、redirect先GETでcurrent/attendance/pendingを再度読む。
7. 会計確定・解除・取消は、書込み後にクライアントイベントで営業中snapshotを再取得する。会計RPC自身の返却がある場合でも一覧UIの反映は別snapshot RPCに依存する。

## 未確認事項・限界

- ローカルサーバー、Supabase、Google Drive、SQLの実行は行っていない。実ネットワークのHAR、各RPCのp50/p95、payload size、SQL EXPLAIN、実cache hit/missは未確認。
- `Sql/store_rpc_functions.sql` 等の集約/旧SQL候補も存在する。本ノートの表は機能別`Sql/store_rpc/`と`Sql/store_settings_functions.sql`を主根拠にした。実DBにどのファイルのどの版が適用済みかは未確認。
- Edge Function許可リストには `store.get_context` などがある一方、マスタリポジトリの通常読取はbootstrap payloadを使う。cache全削除直後、別画面からの初回アクセス、TTL後、または別App Serviceインスタンスでは、ここで示したwarm回数は変わる。
- `ApplicationMemoryCache` はプロセス単位で、App Serviceの複数インスタンス・再起動・slot swap・別ブラウザ間で共有されない。ブラウザlocalStorageも端末/ブラウザごとである。
- Google OAuth/Drive通信はSupabase RPCではないが、領収書プレビューの体感待ちに影響するため含めた。Drive previewのHTML/browser cache挙動およびiframeの同時要求は実ブラウザで未計測。
