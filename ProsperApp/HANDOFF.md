# ProsperApp Handoff

## 現在の位置づけ

このアプリは店舗用アプリのコード基盤です。
領収書簡易入力は一区切りし、現在は店舗アプリ内の `締め作業 > 領収書入力` 機能として扱います。
今後の中心機能は、開け作業、営業中の伝票管理、会計、締め作業です。

## 重要方針

- DB操作は原則 Supabase RPC 経由で行います。
- 設計改善の現在方針は `Docs/設計改善計画.md` を入口にします。営業中から会計完了までの整理では、PageModelからアプリ内変換を切り出し、JSONB/JSON配列の正規化手順を呼び出し側へ散らさない方針を優先します。
- アプリ側から直接テーブルRESTを叩く実装は避けます。
- Supabase RPCのHTTP送信、Edge Functionキー、レスポンスJSON配列/スカラー処理は `ISupabaseRpcClient` / `SupabaseRpcClient` に集約します。アプリからのRPCは必ず `prosper-rpc` Edge Function経由で呼び出し、REST RPC fallbackは持ちません。
- アプリ用RPCは `store` schemaに集約し、Repositoryと `prosper-rpc` allowlistでは `store.get_casts` のようなschema-qualified名を使います。
- `prosper-rpc` で `json` / `jsonb` 引数をSQLへ渡すときは、JSの配列/オブジェクトをそのまま `postgres.js` に渡します。Edge Function側で先に `JSON.stringify` すると二重エンコードされ、Postgres側ではJSON配列ではなくJSON文字列になり、指名追加や注文追加が0件登録になるため避けます。
- RLSは有効化し、アプリ用の操作は `security definer` RPCで制御します。
- 現場画面の初期表示では、既存RPCをPageModel内で並列化して待ち時間を短縮します。卓、商品、キャスト、店舗コンテキスト、店舗一覧などのマスタ系候補はサーバー側 `IMemoryCache` に初回成功時だけ保持し、商品/カテゴリ/キャストのマスタ設定保存が成功した場合だけ関連キャッシュを破棄します。指名バック設定は店舗別マスタDBですが当日の指名入力に使うため現在営業日と同じライフサイクルで保持し、営業日開始、営業日締め、指名バック設定保存の成功時に破棄します。現在営業日は店舗別に締め成功までキャッシュし、営業日開始時は更新、締め成功時は破棄します。複数インスタンスではプロセス単位のキャッシュになるため、他プロセスで締めた営業日は次回プロセス再起動または明示破棄まで残り得ます。RPC失敗や設定未完了の結果はキャッシュしません。
- 現場運用は、営業中画面を操作する `sales-management` 端末1台と、注文入力専用の `order-entry` / `/Orders` 端末複数台を前提にします。localStorageや画面内ドラフトは端末内の復旧用状態として扱い、端末間では直接同期しません。端末間の共有状態はDB/RPC保存後のデータを基準にします。
- 出勤キャスト候補の `store.get_order_attending_casts` は、店舗別・営業日別に `IMemoryCache` へ初回成功時だけ保持します。変更契機は勤怠入力に限られるため、勤怠保存、退勤情報保存、営業日開始、営業日締めの成功時に対象営業日のキャッシュを破棄します。退勤済みキャストも候補に残す仕様なので、退勤済みかどうかだけを理由に候補キャッシュを避ける必要はありません。
- 勤怠時刻刻み、キャスト売上額調整の売上額基準、売上額人数割は端末設定ではなく、`department_master` の店舗別運用設定として管理します。アプリ側では `store.get_context` から取得し、店舗コンテキストキャッシュに載せます。
- 管理者モードでは営業日締め条件を無視できます。画面POSTの `Readiness` ブロックと `store.close_business_day` RPCの条件検証は、同じ管理者モードフラグで迂回します。営業日が存在することと、画面の営業日IDが現在営業日と一致することは引き続き確認します。
- 指名・バックまわりの用語は、会計額へ加算する料金を `指名料金`、指名時にキャストへ支払うバックを `指名バック`、商品注文時にキャストへ支払う通常バックを `ドリンクバック`、その商品注文バック対象が当該伝票の指名キャストだった場合のバックを `担当バック` と呼び分けます。UI文言とドキュメントではこの4語を混同しないでください。
- 営業中トップは営業中操作に必要な一覧だけを取得し、締め作業専用の酒代、締め勤怠、未処理領収書、キャスト売上額調整状態は `/Closing` の各パネル用GET handlerで初期表示後に取得します。伝票追加モーダルの指名候補は初期表示をブロックせず、モーダル表示時にGET handlerで遅延取得します。営業中カラオケ保存は `businessDayId`、`slipId`、`quantity` を `store.save_karaoke_lines` へ送るだけにし、店舗コンテキスト、卓、伝票一覧は再取得しません。数量変更直後は保存状態を「未保存」にし、アプリ内遷移や他フォーム送信の前に未保存分をDB上書き保存します。保存成功後だけ遷移または送信を続行し、保存失敗時は操作を止めて「保存失敗」と表示します。カラオケは `store_item_master.item_type = 'karaoke'` の商品として扱い、保存RPCは同一伝票内のカラオケ注文行を1行に集約します。
- 営業中一覧の `store.get_business_day_slips` と `/Orders` の `store.get_order_entry_slips` は、Razor初期表示をブロックしないようページ用JSON handlerから取得します。初回表示後、フォーカス復帰時、10秒ごとの表示中自動更新で再取得し、保存成功POST直後のサーバー側再ロードは行いません。営業中一覧と `/Orders` の注文対象伝票はどちらも `slipId` 単位で差分反映し、同期のたびに一覧全体を作り直さないでください。`/Orders` で会計済みなどにより候補から消えた伝票は選択と未送信キューから外します。
- 一覧RPCは対象営業日・対象伝票を先に絞ってから関連行を集計します。特に `store.get_business_day_slips` と `store.get_cast_sales_adjustment_slips` は全期間の客、指名、注文、自由入力明細を集計してから最後に絞る形へ戻さないでください。
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
  - RPC結果ごとのキャッシュ/再取得/破棄タイミングは「RPC結果ライフサイクル」にまとめています。
  - 実際の定義は各SQLファイルで確認してください。

- `Sql/agent_schema_reference.sql`
  - 次のエージェント向けの参照用スキーマ集約ファイルです。
  - 実行用ではありません。

- `Sql/store_order_accounting_tables.sql`
  - 店舗営業、伝票、客行、指名、注文、会計系テーブルの作成SQLです。
  - RLS有効化、updated_atトリガー、主要インデックスを含みます。

- `Sql/store_settings_functions.sql`
  - 店舗設定画面用の `store.get_departments()` とデバッグ用の `store.delete_non_master_records(p_department_id, p_confirmation)` RPCです。
  - `department_master` から有効店舗一覧を取得します。

- `Sql/store_rpc_functions.sql`
  - 分割済みRPCファイルの実行順を示す非実行インデックスです。
  - 実行対象は `Sql/store_rpc/*.sql` です。

- `Sql/store_rpc/00_schema.sql`
  - `store` schema作成と旧 `public.*` RPC削除のSQLです。

- `Sql/store_rpc/01_business_day.sql`
  - 営業日、出勤、営業締め系RPCです。

- `Sql/store_rpc/02_store_masters.sql`
  - 卓番、キャスト、商品、指名バック設定、注文入力向け一覧系RPCです。

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

## 実装済み変更と後続整理

### 実装済み

- 勤怠入力のキャスト追加はチェックボックスで複数名を一括登録できます。勤怠時刻は出勤19:00を初期値にし、退勤時刻は未入力を初期値にします。24:00以降は25:00、26:00のように表示します。
- 指名追加の指名種別は店舗別マスタ `store_nomination_back_master` の有効行から選択します。初期定義は本指名、場内指名、同伴4区分で、指名料金は1000円から20000円まで選択できます。指名料金は `store_item_master.item_type = 'nomination_fee'` のシステム商品として `store_order_lines` に自動追加し、会計額へ加算します。指名料金行は注文端末から追加できず、通常注文の数量訂正・削除対象にも含めません。
- `/Orders` は営業中端末の業務フロー外にある注文端末専用画面です。上部タブ、業務フローナビ、フッター、営業中への戻り導線を表示せず、管理者設定だけは画面右上の最小導線として残します。卓番選択、商品一覧、注文キューを3パネル横並びで表示します。画面全体は縦横ともスクロールさせず、各パネル本文だけを縦スクロールします。複数卓の注文を同じキューに入れて一括登録でき、注文キューは卓番ごとにグルーピングして件数と小計を表示します。バック対象商品のキャスト候補には先頭に「なし」を出します。注文端末の商品一覧には標準商品だけを出し、カラオケなどのシステム商品だけで構成されるカテゴリは表示しません。
- 自由入力明細は商品マスタとは別枠の調整明細として扱い、負値も許容して会計合計額へ直接加減します。伝票詳細では専用モーダルから1件ずつ入力して保存し、未保存入力はモーダルを閉じる時点で確認して破棄します。会計時に自由入力明細の未保存チェックを別途行う前提にはしません。
- カラオケは `store_item_master.item_type = 'karaoke'` のシステム商品です。1回200円、サービス料対象、同一伝票1注文行に集約し、時刻列は入店時刻にします。伝票詳細では指名料金と同じ自動システム商品として表示し、数量変更や保存操作は置きません。
- 指名料金は `store_item_master.item_type = 'nomination_fee'` のシステム商品です。指名登録時に指名行 `store_slip_casts.slip_cast_id` を `store_order_lines.source_type = 'nomination_fee'` / `source_id` へ紐づけて1指名1注文行を作ります。会計ではカラオケや指名料金を含む全システム商品を商品小計へ含め、サービス料20%の対象にします。
- 指名種別別キャストバックは、店舗別マスタ `store_nomination_back_master` と営業実績 `store_slip_cast_backs` で扱います。マスタは `nomination_kind`、基本種別、表示名、同伴時刻、バック単価、有効/無効を店舗別に持ちます。`/Management/NominationBacks` でバック単価と有効/無効を管理し、指名登録時に現在の単価を実績行としてスナップショット保存します。
- 営業中画面では会計額を固定オーバーレイ操作中だけ表示し、カラオケ数量は画面内で即時反映して、アプリ内遷移や他フォーム送信の前に保存します。
- マスタ系候補、現在営業日、当日出勤キャスト候補は `IMemoryCache` 対象です。対象は店舗一覧、店舗コンテキスト、卓、キャストマスタ候補、商品候補、商品管理カタログ、キャスト管理一覧、現在営業日、指名バック設定、`store.get_order_attending_casts` の店舗別・営業日別結果です。指名バック設定は現在営業日と同じライフサイクルで保持し、営業日開始、営業日締め、指名バック設定保存の成功時に破棄します。
- 勤怠時刻刻み、キャスト売上額調整の売上額基準、売上額人数割は店舗別マスター値です。`/Settings` には表示せず、`store.get_context` の店舗コンテキストとして利用します。
- 営業中一覧と注文対象伝票は初期表示後のAjax取得と10秒自動更新で扱います。`store.get_business_day_slips` と `store.get_order_entry_slips` はキャッシュせず、保存成功POST直後の再取得を削ります。営業中一覧と `/Orders` の注文対象伝票DOMは `slipId` 単位の差分更新にし、同期のたびに全行を再作成しません。
- `/Settings` の管理者解除後に、デバッグ用の「マスタ以外のテーブルのレコードを削除する」操作を表示します。`store.delete_non_master_records` は選択店舗の営業日、出勤、伝票、注文、会計、バック集計の営業データだけを削除し、`company_master`、`department_master`、`cast_master`、卓、商品、指名バック、支払方法などのマスタ表は削除しません。成功時は現在営業日と指名バック設定のruntimeキャッシュを破棄します。

### 後続仕様・検討候補

- 後で実装するUI/調査メモ:
  - `/Orders` の「読み込み中です」UIが常に残る状態は、初期ロード用要素に識別属性を付け、初期ロード完了後に消えるよう修正済み。
  - 締め作業の各ページ/各パネルが呼ぶRPCを調査し、必要なRPC、取得タイミング、キャッシュ要否、エラー表示を整理する。
  - 営業中トップと伝票詳細の「保存済み」UIは、現在デバウンス自動保存を使っていないため不要。営業中トップのカラオケ状態は未保存/保存中/失敗時だけ表示し、伝票詳細の常時「保存済み」表示は削除済み。
  - `/Orders` の卓番選択表示から入店時刻を外し、代わりに客名を表示する対応済み。客名が取れない場合は人数を表示する。
  - `/Orders` の注文キューは、複数卓の注文が混在しても追いやすいように、卓番ごとのグルーピング、見出し、区切り、件数表示を整理済み。
  - `/Orders` の注文登録成功通知は、まとめて登録した注文の合計件数だけでなく、卓番ごとの登録件数も表示済み。
  - `/Orders` と伝票詳細のオーダー追加モーダルでは、商品カテゴリ名の重複表示を避けるため、カテゴリ本文側の見出しを視覚的に非表示にする対応済み。
  - 全ページで使う統一の「戻る」UIを用意し、戻り先、表示位置、ラベル、アイコン、モバイル時の押しやすさを揃える。
  - 会計処理モーダルとオーダー追加モーダルは、内容の見やすさ、入力順、確認しやすさ、主要操作ボタンの配置を継続して見直す。モーダル幅、区切り、余白は一部調整済み。
  - 途中指名追加がある場合のキャスト売上額調整基準額を整理する。指名追加時点までの会計額はその時点の指名キャストで割り、指名追加後の残額は指名キャスト全員で割る方針を検討する。
  - セット料金、延長料金、各種バックのマスターを整備し、会計、注文バック、キャスト売上、給与計算から参照する基準値と適用条件を一元管理できるようにする。
  - キャストが飲むかどうかの情報をキャストマスターに追加し、注文バック対象キャスト選択時に表示する。
  - 営業日締め完了時に、社長のLINEアカウントへ締め完了通知を送る。
  - 領収書宛名は会計処理の決済方法確認ステップで任意入力し、印字時に `様` を自動付与します。未入力の場合も宛名欄に `様` のみ印字します。但し書きは「ご飲食代として」とし、支払方法表示の `CAT` は「クレジット」として印字済み。店舗住所の印字は `ReceiptPrintRequest` に住所項目がないため未実装。
  - 伝票一覧は値の長さによって行内レイアウトの幅が崩れるため、列幅、折り返し、省略表示を継続して見直す。行高さと列幅は一部調整済み。
  - 伝票詳細ページは行内の余りスペースが大きいため、全体のページ幅を少し狭めて視線移動を減らす対応済み。
  - 伝票一覧からアコーディオン的に伝票詳細を展開し、客、指名、注文、自由入力明細、会計など各項目の編集は専用モーダルに任せる案を、別ブランチまたはprototypeで試す。
  - 伝票一覧の会計済み伝票は、未会計伝票と分けてアコーディオンUIにまとめる。
  - 伝票がない時の「伝票を追加」UIは、上部サマリーの追加導線と重複するため一覧内からは廃止済み。
  - 全体のレスポンスはまだ改善余地があるため、主要操作ごとのRPC回数、画面ロック時間、再読み込み範囲、初期表示データ量、体感待ち時間を整理して改善候補を洗い出す。
- レシートプリンターは会計時の印刷要求データ作成と、SII Web SDK Serverへのブラウザ直接印刷まで実装済みです。`ReceiptPrinter:Enabled = true` の場合、会計確定後に営業中トップへ戻り、同じブラウザから `siiWebSdk.js` の `PrinterManager` 経由で端末ローカルのSII Web SDK Serverへ接続します。印字は80mm幅の紙を前提に、店舗名、宛名、現在時刻、伝票番号、「ご飲食代として」、会計額、支払い方法、内消費税額を出します。宛名は会計処理で入力した値に `様` を付けて印字し、未入力の場合も `様` のみ印字します。支払方法の `CAT` は「クレジット」として印字します。会計額が50,001円以上の場合だけ収入印紙欄を追加します。印刷失敗で会計確定は取り消しません。失敗した印刷要求は同じブラウザのlocalStorageに再印刷待ちとして残し、営業中トップから再印刷または完了扱いにできます。失敗時は、SII Web SDK Server接続、印字データ送信、印刷実行などの失敗ステップと `errorCode` / `errorString` / `errorExtendedString` を営業中トップの警告と再印刷待ち行に表示します。

## SQL参照とDB反映

SQLファイルは現在のDB定義を確認するための参照資料です。DB定義の変更はCodexがSupabase CLIまたはSupabaseコネクタで実行し、実行後にSQLファイルを現在定義へ合わせます。

参照時の順序は以下です。

1. `Sql/store_order_accounting_tables.sql`
2. `Sql/store_settings_functions.sql`
3. `Sql/store_rpc/00_schema.sql`
4. `Sql/store_rpc/01_business_day.sql`
5. `Sql/store_rpc/02_store_masters.sql`
6. `Sql/store_rpc/03_slips.sql`
7. `Sql/store_rpc/04_orders.sql`
8. `Sql/store_rpc/05_checkout.sql`
9. `Sql/store_rpc/06_receipts.sql`
10. `Sql/store_rpc/07_cast_sales_adjustments.sql`
11. `Sql/store_rpc/99_grants.sql`
12. 必要に応じて `Sql/store_table_master_seed.sql`
13. 必要に応じて `Sql/quick_entry_account_master_updates.sql`

`agent_schema_reference.sql` と `store_rpc_functions.sql` は実行対象ではありません。

## 主要RPC

### 店舗設定

- `store.get_departments()`
- `store.delete_non_master_records(p_department_id, p_confirmation)`

### 店舗コンテキスト・営業日

- `store.get_context(p_department_id)`
- `store.get_current_business_day(p_department_id)`
- `store.open_business_day(p_department_id, p_business_date, p_memo)`
- `store.open_business_day_with_attendance(p_department_id, p_business_date, p_attendance_entries, p_memo)`
- `store.get_open_slip_count(p_department_id, p_business_day_id)`
- `store.get_business_day_drink_delivery_status(p_department_id, p_business_day_id)`
- `store.save_business_day_drink_delivery_amount(p_department_id, p_business_day_id, p_drink_delivery_amount)`
- `store.get_business_day_closing_attendance(p_department_id, p_business_day_id)`
- `store.save_business_day_closing_attendance(p_department_id, p_business_day_id, p_attendance_entries)`
- `store.get_business_day_cast_sales_adjustment_status(p_department_id, p_business_day_id)`
- `store.get_cast_sales_adjustment_slips(p_department_id, p_business_day_id)`
- `store.get_cast_sales_adjustment_detail(p_department_id, p_slip_id)`
- `store.save_cast_sales_adjustment(p_department_id, p_slip_id, p_adjustments, p_source_amount_type, p_split_mode)`
- `store.close_business_day(p_department_id, p_business_day_id, p_memo, p_pending_receipt_status, p_ignore_closing_requirements)`

### 伝票

- `store.get_tables(p_department_id)`
- `store.get_casts(p_department_id)`
  - 有効店舗に所属する全会社の有効キャストを返します。
  - ヘルプ対応のため、現在店舗所属キャストだけに限定しません。
- `store.get_casts_admin(p_department_id)`
  - キャスト管理画面用に、現在店舗所属キャストだけを返します。
- `store.get_business_day_slips(p_department_id, p_business_day_id)`
- `store.get_order_entry_slips(p_department_id, p_business_day_id)`
- `store.get_order_items(p_department_id)`
- `store.get_item_admin_catalog(p_department_id)`
- `store.get_nomination_back_master(p_department_id)`
- `store.save_nomination_back_master(p_department_id, p_settings)`
- `store.upsert_item_category(p_department_id, p_item_category_id, p_category_code, p_category_name, p_sort_order, p_is_active)`
- `store.upsert_item(p_department_id, p_item_id, p_item_category_id, p_item_name, p_default_price, p_is_active, p_is_cast_back_target, p_cast_back_regular_unit_amount, p_cast_back_nomination_unit_amount, p_cast_back_type)`
- `store.delete_item(p_department_id, p_item_id)`
- `store.add_order_lines(p_department_id, p_slip_id, p_order_lines)`
  - 注文端末/オーダー追加から登録できるのは `store_item_master.item_type = 'standard'` の標準商品だけです。カラオケなどのシステム商品は `store.save_karaoke_lines` など専用RPCで扱います。
- `store.create_slip(p_department_id, p_table_id, p_opened_at, p_customer_labels, p_cast_nominations, p_memo)`
  - `p_cast_nominations` は `cast_id`, `nomination_kind`, `nomination_price` を持つJSON配列です。`nomination_kind` から店舗別マスタの基本種別と同伴時刻を解決します。
  - `nomination_price` はUI/ドキュメント上の指名料金です。1000円から20000円まで1000円刻みで、`item_type = 'nomination_fee'` のシステム商品として注文行へ自動追加し、会計額へ加算します。
  - 有効な指名バック設定がありバック単価が0円より大きい場合、`store_slip_cast_backs` に現在単価の実績を作成します。
- `store.save_slip_adjustments(p_department_id, p_slip_id, p_adjustment_lines)`
  - 通常商品とは別枠の自由入力明細を一括保存する互換用RPCです。商品マスタへは登録しません。
- `store.add_slip_adjustment(p_department_id, p_slip_id, p_line_name, p_amount)`
  - 伝票詳細の自由入力明細モーダルから1件追加します。商品マスタへは登録せず、負値も許容します。
  - `amount` は負値を許容し、会計合計額へ直接加減します。
- `store.save_karaoke_lines(p_department_id, p_business_day_id, p_karaoke_lines)`
  - カラオケ商品の注文行を伝票単位で一括保存します。
  - カラオケは `store_item_master.item_type = 'karaoke'`、1回200円固定、サービス料対象です。
  - 同一伝票内ではカラオケ注文行を1行に集約し、`ordered_at` は入店時刻に合わせます。数量0はアクティブ行を残しません。
- `store.save_order_line_quantities(p_department_id, p_slip_id, p_order_lines)`
  - 伝票詳細の訂正モードから通常注文行の数量だけを訂正します。
  - カラオケなどのシステム商品は対象外です。数量0は注文取消と同じく対象注文行と紐づくバック実績を `voided` にします。
- `store.confirm_checkout(p_department_id, p_slip_id, p_closed_at, p_payments, p_received_amount, p_confirmed_snapshot)`
- `store.cancel_checkout(p_department_id, p_slip_id)`
  - 会計取消は、開いている営業日の会計済み伝票だけを対象にします。
  - 確定済み会計と支払明細を `cancelled` にし、伝票を `open` へ戻します。客行の退店状態と退店時刻は変更しません。会計に紐づくキャスト売上額調整は削除してリセットし、再会計後に必要なら締め作業で再保存します。再会計できるよう `store_checkouts` は `cancelled` 以外の会計だけが伝票単位で一意です。

### 領収書

- `store.get_pending_receipts(p_department_id, p_status)`
- `store.quick_enter_receipt(p_department_id, p_document_id, p_payment_date, p_amount, p_account_subject, p_description, p_group_code, p_journal_payload, p_status)`
- `store.mark_receipt_scan_mistake(p_department_id, p_document_id, p_status)`
  - 領収書の管理入力は、DocManagementの `save_journal_payload` 契約に従ったpayloadを作成し、ProsperAppの `store.quick_enter_receipt` RPCへ渡します。DocManagementアプリや `document-api` へ直接送信しません。
  - スキャンミス除外はDriveファイルを削除せず、DB上のステータス更新で入力対象から外します。

## 画面構成

### UI設計方針

- 店舗業務の大きな状態は `営業中 -> 締め作業` を基本にします。営業日は現在時刻の正午切替ルールで決め、最初の業務入力POSTで自動作成します。
- 営業端末UIは11インチ程度の横置きタブレットを主対象にします。15.6インチ以上のブラウザ表示では横に広げすぎず、11インチ横置きで一覧、入力、主要操作が一画面内で追いやすい密度を基準にします。スマホ縦表示は補助・緊急操作用のfallbackとして崩さず残しますが、快適操作の主対象にはしません。
- 現場画面は原則として `状態サマリー / 作業パネル / 実行操作` の3層で揃えます。見出し、対象営業日や卓番、件数、主要CTAは画面ごとに位置を変えず、作業パネルの中に同じ見た目で配置します。
- パネル見出しは「名称 + 件数/金額などの状態 + 追加/保存などの操作」の順で揃えます。パネル内の保存、未保存、保存中、保存失敗は利用者に見えるテキストまたはバッジで表示し、`aria-live` を付けて非同期保存の状態も伝えます。
- 横置きタブレットでは横スクロールを日常操作に使わせない方針です。卓番、状態、時刻、客、指名、会計、数量など判断に必要な列を優先し、メモや補足は省略、折り返し、詳細画面、モーダルに逃がします。スマホ縦だけは表の横スクロールをfallbackとして許容します。
- ボタン階層は全現場画面で固定します。画面やパネルの主要保存/確定は `btn-primary`、戻る/閉じる/クリアなどの補助操作は `btn-outline-secondary`、削除/除外/営業日締めなどの危険操作は danger 系にします。主要CTAはパネル下部やモーダルフッターで探さなくてよい位置に置きます。
- モーダルはタッチ操作を前提に、本文だけをスクロールさせ、フッターの閉じる/保存/確定操作は常に同じ下部位置に残します。検索、候補、数量、確定ボタンの順序も画面間で揃えます。
- 縦向きfallbackは、全機能を快適にするのではなく、閲覧、最低限の入力、保存、戻る操作が破綻しないことを基準にします。横置きでの情報密度や操作速度を犠牲にしてまで縦表示へ最適化しません。
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
  - 伝票追加モーダルは11インチ横置きでも入力順が追いやすいよう、入店情報、卓番、客情報、指名情報、メモを1カラムで縦に並べます。
  - 営業中の営業日がない場合は「最初の入力で営業日を自動作成」と表示し、明示的な営業日開始ボタンは置きません。
  - 酒代、領収書、営業日締めなど勤怠以外の締め作業は `/Closing` に集約します。
  - 伝票一覧は大きな枠付きパネルにまとめず、スタッフが一覧からすぐ伝票へ入れる構成を優先します。
  - 伝票一覧の会計額は常に隠し、右下寄りの固定オーバーレイボタンに触れている間だけ表示します。
  - 営業中カラオケは伝票行の `+` / `-` で画面内ドラフトを即時更新し、保存状態を「未保存」にします。短周期の自動保存は行わず、アプリ内遷移や他フォーム送信の前に、現在表示している数量を `store.save_karaoke_lines` へ送ってDB上書き保存します。保存成功後だけ遷移または送信を続行し、失敗時は操作を止めて `localStorage` の未送信ドラフトを残します。

- `/Management`
  - 上部タブの `マスタ設定` 入口です。
  - キャスト情報、商品情報、指名バック設定は、営業日前の必須作業ではなく、ここから任意のタイミングで開く導線にします。
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
  - 指名料金は `item_type = 'nomination_fee'` のシステム商品として商品マスタに置きます。価格は指名登録時の選択額を注文行へスナップショットし、カラオケと同じく商品小計とサービス料20%の対象にします。通常の商品削除RPCでは削除できません。
  - 注文履歴は `item_name_snapshot` / `unit_price` / `amount` を保持するため、商品マスタを再参照しません。
  - 商品削除は `store.delete_item` で商品マスタ行を削除し、既存注文行の `item_id` は切り離します。

- `/Management/NominationBacks`
  - 指名種別別キャストバックの店舗別マスタです。
  - 店舗別DBマスタに定義された指名種別についてバック単価と有効/無効を保存します。初期値は本指名、場内指名、同伴4区分で、バック単価はいずれも1000円です。
  - 保存成功時は指名バック設定キャッシュだけを破棄します。商品候補やキャスト候補のキャッシュは破棄しません。指名バック設定キャッシュは営業日開始、営業日締めの成功時にも破棄します。

- `/Slips/Edit`
  - 伝票詳細、客追加、指名追加、オーダー追加、会計処理。
  - 営業中画面の当日伝票一覧から遷移します。
  - 指名追加は当日出勤キャストをコンボボックスから選択し、店舗別DBマスタの有効な指名種別と1000円から20000円までの指名料金を選択します。初期定義では本指名が先頭です。
  - 自由入力明細は通常商品とは別枠で、専用モーダルから1件ずつ追加し、伝票/会計に表示します。
  - カラオケは商品としてオーダー一覧に表示し、時刻列は入店時刻に固定します。異なるタイミングで追加したカラオケも同一伝票内では1行に集約します。伝票詳細では自動システム商品として表示するだけにし、数量変更や保存操作は営業中トップに集約します。
  - 指名料金は指名登録時にシステム商品としてオーダー一覧へ自動表示します。通常注文行は訂正モードで数量のみ変更できます。数量0は注文取消扱いです。カラオケや指名料金などのシステム商品は通常注文の訂正・削除対象に含めません。
  - 会計後の会計処理ボタンは会計取消ボタンとして扱います。押下時は売上、残金、印刷済み領収書などへの影響を確認し、実行後は伝票を営業中へ戻して再会計できる状態にします。
  - 会計確定後は `ReceiptPrinter:Enabled = true` の場合だけ、領収書印刷要求を作成します。会計確定後に営業中トップへ戻ったブラウザが、SII Web SDK Serverへ直接印刷します。80mm幅の紙に、店舗名、宛名、現在時刻、伝票番号、「ご飲食代として」、会計額、支払い方法、内消費税額を印字し、宛名は会計処理で入力した値に `様` を付け、未入力の場合も `様` のみ表示します。支払方法の `CAT` は「クレジット」として表示します。会計額50,001円以上では収入印紙欄を追加します。印刷失敗で会計確定は取り消さず、同じブラウザの営業中トップに再印刷待ちとして残します。失敗時はSII Web SDKの失敗ステップと `errorCode` / `errorString` / `errorExtendedString` を画面に表示します。

- `/Orders`
  - オーダー入力。営業中端末の上部タブや業務フローナビには含めない注文端末専用画面です。
  - マスタ設定や営業中への戻り導線は表示しません。管理者設定は右上のボタンとして残します。
  - open伝票を卓番として選択し、カテゴリ別の商品ボタンから注文キューへ追加します。
  - 商品ボタンは標準商品だけを表示します。システム商品は注文キューへ追加できず、注文可能な商品が1件もないカテゴリは表示しません。
  - 卓番選択、商品一覧、注文キューを3パネル横並びで表示し、画面全体は縦横とも固定したまま、それぞれのパネル本文だけを縦スクロールします。
  - 同じ商品は数量加算し、一括登録後は卓番選択へ戻ります。

- `/Closing`
  - 締め作業画面。
  - 酒代入力、勤怠確認、キャスト売上額調整、領収書入力を縦並びの独立パネルで表示します。
  - 初期表示では現在営業日と締めメモだけを取得し、各パネルの状態は表示後、フォーカス復帰時、30秒ごとの表示中自動更新でJSON handlerから取得します。
  - 営業日締めは通常の作業パネルから分離し、締め条件と最終実行ボタンを下部にまとめます。
  - 酒代入力、勤怠確認、キャスト売上額調整、領収書入力は締め前の必須作業です。未完了の必須作業は赤、確認対象は橙、完了は緑で表示します。
  - キャスト売上額調整は `/Closing/CastSalesAdjustment` の専用ページで、会計済みかつ指名キャストがいる伝票を一覧表示し、客名とキャストごとの売上分配額を一覧上で確認できるようにします。売上額調整は行末のボタンから開くモーダルで保存します。
  - 領収書入力は未入力がある場合に要入力として表示し、営業日締めのブロック条件にします。
  - 営業日締めは、通常モードでは未会計伝票0、酒代入力済み、勤怠1名以上、退勤未入力0、キャスト売上額調整済み、領収書入力完了を満たした場合だけ実行できます。画面POSTと `store.close_business_day` RPCの両方で同じ条件を検証します。管理者モードでは締め条件を無視して実行できます。

- `/Closing/Attendance`
  - 出勤登録を統合した勤怠入力画面。
  - 出勤者選択、出勤時刻、退勤時刻、送り利用有無を同じ画面で入力します。
  - 退勤時刻は営業中の途中保存では空でも保存でき、締め前チェックで未入力警告を出します。
  - 退勤時刻の選択肢は店舗別マスターの `attendance_minute_step` に従います。

- `/Closing/Receipts`
  - 領収書簡易入力。
  - Google Driveプレビュー、DocManagement契約payload作成、Supabase RPC更新、スキャンミス除外、PDF先読みキャッシュを含みます。
  - 入力保存時はDocManagement契約の `journal_entries`、`journal_entry_lines`、`document_journal_links` を `store.quick_enter_receipt` RPC payloadに含め、領収書ステータスを完了へ更新します。
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
  - 利用店舗、画面モード、管理者モードを端末ローカル保存します。勤怠時刻刻み、売上額基準、売上額人数割は店舗別マスターで管理します。
  - 端末が管理者モードの場合は、管理者設定のパスワード入力をスキップして設定フォームを開きます。
  - 管理者設定の保存後は端末設定Cookieを更新し、共通レイアウトでlocalStorageへ同期してからトップ `/` へ遷移します。

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

SII Web SDK Server経由の会計時領収書印刷を使う場合は以下も設定します。

- `ReceiptPrinter__Enabled=true`
- `ReceiptPrinter__BrowserSdkScriptUrl=https://www.sii-ps.com/sample/websdk/siiWebSdk.js`
- `ReceiptPrinter__BrowserWebSocketHost=localhost`
  - Android営業中端末上でSII Web SDK Serverを起動して使う前提です。別hostで動かす場合だけ変更します。
- `ReceiptPrinter__BrowserCodePage`
  - 通常は空でよいです。実機で日本語印字に調整が必要な場合だけSII側の指定値を入れます。
- `ReceiptPrinter__BrowserInternationalCharacter`
  - 通常は空でよいです。実機で国際文字設定が必要な場合だけSII側の指定値を入れます。
- `ReceiptPrinter__LineWidth=48`

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
