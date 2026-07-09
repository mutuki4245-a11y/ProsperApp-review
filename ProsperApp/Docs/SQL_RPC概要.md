# ProsperApp SQL/RPC概要

作成日: 2026-07-03
対象: ProsperApp 店舗用アプリ
用途: SQL定義とRPCの俯瞰

## 1. 文書の位置づけ

本書は `Sql/` 配下にある店舗アプリ用SQL定義とRPCの概要をまとめる。実装時に読む入口として使い、最終的な正は各SQLファイルとアプリ側Repository実装で確認する。

DB操作は原則Supabase RPC経由で行う。アプリからのRPC呼び出しは `ISupabaseRpcClient` / `SupabaseRpcClient` に集約し、`prosper-rpc` Edge Function経由で実行する。直接テーブルRESTやREST RPC fallbackは持たない。

アプリ用RPCは `store` schemaに集約し、RPC名は `store.get_casts` のようにschemaで用途境界を示す。各RPCは `security definer` と `set search_path = public` を前提にし、`Sql/store_rpc/99_grants.sql` で `public`、`anon`、`authenticated`、`service_role` からの直接実行権限と `store` schema usageを剥奪する。アプリから直接PostgREST RPCを呼べる状態に戻さない。

## 2. SQLファイルの役割

| ファイル | 役割 |
| --- | --- |
| `Sql/store_order_accounting_tables.sql` | 店舗営業、伝票、客行、指名、注文、自由入力調整、会計、締め調整のテーブル定義。`department_master` の店舗別運用設定列、RLS有効化、`updated_at` トリガー、主要インデックスを含む。 |
| `Sql/store_settings_functions.sql` | 店舗設定画面用RPC。`store.get_departments()` で有効店舗一覧を返し、`store.delete_non_master_records` でデバッグ用に選択店舗の営業データを削除する。 |
| `Sql/store_rpc_functions.sql` | 分割済みRPCファイルの実行順を示す非実行インデックス。実行対象ではない。 |
| `Sql/store_rpc/00_schema.sql` | `store` schemaを作成し、旧 `public.*` RPCを削除する。 |
| `Sql/store_rpc/01_business_day.sql` | 店舗コンテキスト、営業日開始/取得/締め、勤怠、酒代、未会計伝票数を扱う。 |
| `Sql/store_rpc/02_store_masters.sql` | 卓、キャスト、商品、商品管理、指名バック設定、営業中/注文入力向け伝票一覧を扱う。 |
| `Sql/store_rpc/03_slips.sql` | 伝票詳細、客追加/退店、指名追加、自由入力調整、カラオケ商品数量、通常注文数量訂正、客名更新、注文取消を扱う。 |
| `Sql/store_rpc/04_orders.sql` | 注文登録と、バックキャスト候補用の当日出勤キャスト取得を扱う。 |
| `Sql/store_rpc/05_checkout.sql` | 会計確定、会計取消、伝票作成を扱う。 |
| `Sql/store_rpc/06_receipts.sql` | 領収書入力、簡易入力、スキャンミス除外を扱う。 |
| `Sql/store_rpc/07_cast_sales_adjustments.sql` | 締め作業のキャスト売上額調整を扱う。 |
| `Sql/store_rpc/99_grants.sql` | アプリRPCの直接PostgREST実行権限を剥奪する。RPC追加時はこの対象一覧も更新する。 |
| `Sql/store_table_master_seed.sql` | mieu本店の卓番マスタ初期データ。 |
| `Sql/quick_entry_account_master_updates.sql` | 領収書簡易入力UIで使う科目・補助科目の追加更新SQL。実行前に文字化け有無を確認する。 |
| `Sql/agent_schema_reference.sql` | エージェント向けの参照用スキーマ集約ファイル。実行対象ではない。 |

DB反映時の基本順序は以下。

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

## 3. テーブル概要

| 分類 | テーブル | 概要 |
| --- | --- | --- |
| 既存マスタ | `department_master` | 店舗マスタ。店舗別運用設定として勤怠時刻刻み、キャスト売上額調整の売上額基準、売上額人数割を持つ。 |
| マスタ | `store_table_master` | 店舗ごとの卓番。 |
| マスタ | `cast_master` | キャスト。店舗所属と表示順を持つ。 |
| マスタ | `store_item_category_master` | 商品カテゴリ。 |
| マスタ | `store_item_master` | 商品マスタ。価格、商品種別、キャストバック対象、バック単価、バック種別を持つ。カラオケは `item_type = 'karaoke'`、指名料金は `item_type = 'nomination_fee'` のシステム商品。 |
| マスタ | `store_nomination_back_master` | 店舗別の指名種別と指名バック設定。`nomination_kind`、基本種別、表示名、同伴時刻、バック単価、有効/無効を持つ。 |
| マスタ | `payment_method_master` | 支払方法マスタ。 |
| 営業日/勤怠 | `store_business_days` | 店舗ごとの営業日。営業開始/締め状態、メモ、酒代などを持つ。 |
| 営業日/勤怠 | `store_cast_attendance` | 営業日ごとのキャスト出退勤。 |
| 伝票 | `store_slips` | 卓単位の伝票ヘッダ。会計額列は持たず、会計額はRPCで都度集計する。 |
| 伝票 | `store_slip_customers` | 伝票内の客行。入退店状態と表示名を持つ。 |
| 伝票 | `store_slip_casts` | 伝票に紐づく指名。指名種別、同伴時刻区分、指名料金の選択額を持つ。 |
| 注文/バック | `store_order_lines` | 商品注文行。数量、単価、取消状態を持つ。指名料金は `source_type = 'nomination_fee'` / `source_id = slip_cast_id` で指名行に紐づく。 |
| 注文/バック | `store_order_line_cast_backs` | 注文行に紐づくバック対象キャスト。通常の商品注文バックはドリンクバック、対象キャストが当該伝票の指名キャストだった場合は担当バックとして扱う。 |
| 指名バック | `store_slip_cast_backs` | 指名行に紐づくキャストバック実績。指名登録時点の店舗別マスタ単価をスナップショット保存する。 |
| 自由入力調整 | `store_slip_charge_lines` | 商品マスタとは別枠の伝票調整行。現行運用では `adjustment` を扱い、旧カラオケ別枠行は注文行へ移行してvoid化する。 |
| 会計 | `store_checkouts` | 会計確定結果。会計時点の小計、サービス料、合計を保存する。 |
| 会計 | `store_checkout_payments` | 会計に紐づく支払方法別明細。 |
| 締め調整 | `store_slip_cast_sales_adjustments` | 締め作業で行うキャスト売上額調整。 |

共通設計として、店舗営業系テーブルは `company_id`、`department_id`、必要に応じて `business_day_id` を持つ。主要テーブルはRLSを有効化し、`public.set_updated_at()` による `updated_at` 更新トリガーを持つ。検索頻度が高い有効マスタ、営業日、営業中伝票、伝票明細、会計、締め調整には用途別インデックスを置く。

## 4. 会計額の扱い

`store_slips` に会計額を保持する列はない。営業中一覧の `accounting_amount` は `store.get_business_day_slips` が返す表示用の集計値であり、永続化された確定額ではない。

営業中の会計額は以下を元に集計する。

- 有効な `store_order_lines` 全体の注文小計。標準商品、カラオケ、指名料金などのシステム商品を含む。
- 注文小計に対する20%サービス料
- 有効な `store_slip_charge_lines` のうち自由入力調整の合計

カラオケは `store_item_master.item_type = 'karaoke'` の商品として扱い、`store_order_lines` に1伝票1行で集約する。単価は1回200円固定で、注文小計に含まれるためサービス料20%の対象になる。`ordered_at` は入店時刻に合わせ、異なるタイミングで追加したカラオケも同一伝票では数量だけを更新する。

指名料金は `store_item_master.item_type = 'nomination_fee'` のシステム商品として扱い、指名登録時に `store_order_lines` へ1指名1行で自動追加する。商品注文端末からは注文できず、通常注文の数量訂正・削除対象にも含めない。カラオケと指名料金を含むシステム商品は、会計では標準商品と同じく商品小計とサービス料20%の対象にする。

用語は、会計額へ加算する料金を `指名料金`、指名時にキャストへ支払うバックを `指名バック`、商品注文時にキャストへ支払う通常バックを `ドリンクバック`、商品注文バック対象が当該伝票の指名キャストだった場合のバックを `担当バック` と呼び分ける。

会計確定時は `store.confirm_checkout` が注文、指名、自由入力調整を再集計し、支払合計と照合したうえで `store_checkouts.subtotal_amount`、`store_checkouts.service_tax_amount`、`store_checkouts.total_amount` と `store_checkout_payments` を保存する。営業中一覧の表示額を確定額として信用しない。

## 5. RPC概要

### 店舗設定

| RPC | 主な用途 |
| --- | --- |
| `store.get_departments` | 有効な店舗一覧を返す。 |
| `store.delete_non_master_records` | デバッグ用に選択店舗の営業日、出勤、伝票、注文、会計、バック集計のレコードを削除する。マスタ表は削除しない。 |

### 店舗コンテキスト・営業日

| RPC | 主な用途 |
| --- | --- |
| `store.get_context` | 店舗IDから店舗コンテキストを返す。勤怠時刻刻み、キャスト売上額調整の売上額基準、売上額人数割も返す。 |
| `store.get_current_business_day` | 未締めの現在営業日を返す。締めるまでキャッシュ対象。 |
| `store.open_business_day` | 営業日を開始する。 |
| `store.open_business_day_with_attendance` | 営業日開始と勤怠一括登録を行う。 |
| `store.save_business_day_attendance` | 営業中の勤怠入力を保存する。 |
| `store.get_business_day_closing_attendance` | 締め作業用の勤怠一覧を返す。 |
| `store.save_business_day_closing_attendance` | 締め作業用の勤怠修正を保存する。 |
| `store.get_open_slip_count` | 未会計伝票数を返す。 |
| `store.get_business_day_drink_delivery_status` | 酒代入力状態を返す。 |
| `store.save_business_day_drink_delivery_amount` | 酒代を保存する。 |
| `store.close_business_day` | 営業日を締める。通常モードでは締め条件を検証し、管理者モードからは `p_ignore_closing_requirements` で条件検証を無視できる。 |

### マスタ・一覧

| RPC | 主な用途 |
| --- | --- |
| `store.get_tables` | 卓番候補を返す。 |
| `store.get_casts` | キャスト候補を返す。ヘルプ対応のため会社を跨いだ全有効店舗所属キャストを含む。 |
| `store.get_casts_admin` | キャスト管理画面用に、現在店舗所属キャストだけを返す。 |
| `store.create_cast` | キャストを作成する。 |
| `store.delete_cast` | キャストを論理削除する。`cast_master.status = 'inactive'`、`is_active = false` に更新する。 |
| `store.get_business_day_slips` | 営業中画面向けの伝票一覧と会計表示額を返す。 |
| `store.get_order_entry_slips` | `/Orders` 向けの注文入力対象伝票一覧を返す。 |
| `store.get_order_items` | 注文入力用の商品一覧を返す。標準商品だけを返し、カラオケなどのシステム商品は返さない。 |
| `store.get_item_admin_catalog` | 商品管理画面用のカテゴリ/商品一覧を返す。 |
| `store.get_nomination_back_master` | 指名バック設定画面と指名入力用に、店舗別DBマスタの指名種別候補とバック単価を返す。 |
| `store.save_nomination_back_master` | 指名種別候補と指名バック設定を店舗別に保存する。 |
| `store.upsert_item_category` | 商品カテゴリを作成/更新する。 |
| `store.upsert_item` | 商品を作成/更新する。 |
| `store.delete_item` | 商品を削除または無効化する。 |
| `store.reorder_items` | 商品表示順を更新する。 |

### 伝票

| RPC | 主な用途 |
| --- | --- |
| `store.get_slip_detail` | 伝票詳細、客行、指名、注文、自由入力調整、会計候補を返す。カラオケは注文行の `item_type = 'karaoke'`、指名料金は `item_type = 'nomination_fee'` で判定する。 |
| `store.create_slip` | 伝票を作成する。初期指名がある場合は指名料金のシステム注文行と、指名バック設定に基づく `store_slip_cast_backs` を作成する。 |
| `store.add_slip_customers` | 既存伝票へ客行を追加する。 |
| `store.add_slip_nominations` | 既存伝票へ指名を追加する。指名料金のシステム注文行と、指名バック設定が有効かつ0円より大きい場合は `store_slip_cast_backs` を作成する。 |
| `store.leave_slip_customer` | 客行を退店扱いにする。 |
| `store.save_slip_adjustments` | 自由入力の会計調整行を保存する。 |
| `store.save_karaoke_lines` | 営業日内のカラオケ商品数量を伝票単位のJSON payloadで保存する。同一伝票のカラオケ注文行は1行に集約する。 |
| `store.save_order_line_quantities` | 伝票詳細の訂正モードから通常注文行の数量を保存する。数量0は対象注文行と紐づくバック実績を取消扱いにする。 |
| `store.update_slip_customer_label` | 客行の表示名を更新する。 |
| `store.void_order_line` | 注文行を取消する。 |

### 注文

| RPC | 主な用途 |
| --- | --- |
| `store.get_order_attending_casts` | 当日出勤キャストを返す。退勤済みも候補に残す。 |
| `store.add_order_lines` | 注文行とバック対象キャストを登録する。`p_order_lines` に伝票IDを含められるため、`/Orders` では複数卓のキューをまとめて登録できる。登録できる商品は標準商品だけで、システム商品は拒否する。 |

### 会計

| RPC | 主な用途 |
| --- | --- |
| `store.confirm_checkout` | 会計額を再計算し、支払合計を検証して会計確定する。 |
| `store.cancel_checkout` | 開いている営業日の会計済み伝票を営業中へ戻す。会計と支払明細は `cancelled` にし、客行の退店状態と退店時刻は変更しない。会計に紐づくキャスト売上額調整は削除してリセットする。 |

### 領収書入力

| RPC | 主な用途 |
| --- | --- |
| `store.get_pending_receipts` | 未処理領収書一覧を返す。 |
| `store.quick_enter_receipt` | 領収書を簡易入力する。DocManagement連携用payload引数を受け取る。 |
| `store.mark_receipt_scan_mistake` | 領収書をスキャンミスとして除外する。 |

### 締め作業のキャスト売上額調整

| RPC | 主な用途 |
| --- | --- |
| `store.get_business_day_cast_sales_adjustment_status` | 締め作業画面に表示するキャスト売上額調整状態を返す。 |
| `store.get_cast_sales_adjustment_slips` | 調整対象伝票一覧を返す。 |
| `store.get_cast_sales_adjustment_detail` | 伝票単位の調整詳細を返す。 |
| `store.save_cast_sales_adjustment` | キャスト売上額調整を保存する。 |

## 6. 呼び出し経路

Razor PageのPageModelはRepositoryを呼び、Repositoryが `ISupabaseRpcClient` を通じてRPC名とpayloadを `prosper-rpc` Edge Functionへ渡す。Edge Function側で許可済みRPCだけを実行し、Repositoryは戻り値のJSON配列またはスカラーをDTOへ変換する。

設定キーやsecretの値はリポジトリに置かない。Azure環境変数とSupabase Edge Function Secretsの名称・値は運用環境で一致させる。

## 7. RPC更新時の注意

RPCを追加/変更するときは、以下を同じタスク内で揃える。

1. 対象の `Sql/store_rpc/*.sql` または `Sql/store_settings_functions.sql`
2. 新規schemaや旧RPC削除が必要な場合は `Sql/store_rpc/00_schema.sql`
3. `Sql/store_rpc/99_grants.sql` の対象RPC一覧
4. `prosper-rpc` Edge Function側の許可RPC一覧
5. アプリ側Repository、DTO、JSONパース処理
6. 必要に応じて `HANDOFF.md`、`Docs/システム仕様書.md`、本書

一覧RPCは対象営業日や対象伝票を先に絞ってから関連行を集計する。特に `store.get_business_day_slips` と `store.get_cast_sales_adjustment_slips` は、全期間の客、指名、注文、自由入力明細を集計してから最後に絞る形へ戻さない。

アプリ側では、店舗一覧、店舗コンテキスト、卓、キャストマスタ候補、商品候補、商品管理カタログ、キャスト管理一覧、指名バック設定、現在営業日、当日出勤キャスト候補を `IMemoryCache` の対象として扱う。RPC失敗や設定未完了の結果はキャッシュしない。指名バック設定は店舗別マスタDBだが、当日の指名入力に使うため現在営業日と同じライフサイクルで保持し、営業日開始、営業日締め、指名バック設定保存の成功時に破棄する。商品/カテゴリ保存、商品削除、商品並び順保存、キャスト登録/削除などその他の破棄契機は `HANDOFF.md` の重要方針に従う。

`store.get_order_attending_casts` は店舗別・営業日別にアプリ側でキャッシュする。RPC定義変更は不要で、勤怠保存、退勤情報保存、営業日開始、営業日締めの成功時に対象営業日のキャッシュを破棄する。

`store.get_business_day_slips` と `store.get_order_entry_slips` はキャッシュ対象にしない。アプリ側ではRazor初期表示をブロックせず、ページ用JSON handlerから初回Ajax、フォーカス復帰、30秒ごとの表示中自動更新で取得する。保存成功POST直後の同期再取得は行わない。

## 8. RPC結果ライフサイクル

Repositoryが受け取ったRPC結果は、以下のライフサイクルで扱う。キャッシュはアプリサーバープロセス内の `IMemoryCache` であり、プロセス再起動や別インスタンスには共有されない。RPC失敗、設定未完了、検証に使えない空結果はキャッシュしない。

| RPC | 結果の保持単位 | ライフサイクル |
| --- | --- | --- |
| `store.get_departments` | 店舗一覧マスタ。アプリプロセス単位。 | 初回成功時に保持する。アプリ内に店舗マスタ更新画面がないため明示破棄はせず、プロセス再起動または将来の店舗更新機能で更新する。 |
| `store.delete_non_master_records` | デバッグ削除結果のみ。 | 戻り値のテーブル別削除件数を画面結果表示に使い、キャッシュしない。成功時に現在営業日と指名バック設定のruntimeキャッシュを破棄する。 |
| `store.get_context` | 店舗別マスタ。`department_id` 単位。 | 通常画面では初回成功時に保持する。店舗別運用設定がアプリ内で更新された場合は破棄が必要。領収書保存時の会社ID取得だけは現状キャッシュを経由せず都度取得する。 |
| `store.get_tables` | 店舗別マスタ。`department_id` 単位。 | 初回成功時に保持する。卓マスタ更新をアプリ内で扱うまでは明示破棄しない。 |
| `store.get_casts` | 店舗別キャスト候補。`department_id` 単位。 | 初回成功時に保持する。`store.create_cast` / `store.delete_cast` 成功時に破棄する。 |
| `store.get_casts_admin` | 店舗別キャスト管理一覧。`department_id` 単位。 | 初回成功時に保持する。`store.create_cast` / `store.delete_cast` 成功時に `store.get_casts` と同時に破棄する。 |
| `store.create_cast` | 保存結果のみ。 | 戻り値の `cast_id` を画面結果判定に使い、キャッシュしない。成功時にキャスト候補/管理一覧キャッシュを破棄する。 |
| `store.delete_cast` | 保存結果のみ。 | 戻り値の `cast_id` を画面結果判定に使い、キャッシュしない。成功時にキャスト候補/管理一覧キャッシュを破棄する。 |
| `store.get_order_items` | 店舗別の注文可能商品候補。`department_id` 単位。標準商品だけを返す。 | 初回成功時に保持する。商品カテゴリ/商品保存、商品削除、商品並び順保存の成功時に破棄する。 |
| `store.get_item_admin_catalog` | 店舗別商品管理カタログ。`department_id` 単位。 | 初回成功時に保持する。`store.get_order_items` と同じ商品関連更新の成功時に破棄する。 |
| `store.upsert_item_category` | 保存結果のみ。 | 戻り値の `item_category_id` を画面結果判定に使い、キャッシュしない。成功時に商品候補/商品管理カタログキャッシュを破棄する。 |
| `store.upsert_item` | 保存結果のみ。 | 戻り値の `item_id` を画面結果判定に使い、キャッシュしない。成功時に商品候補/商品管理カタログキャッシュを破棄する。 |
| `store.delete_item` | 保存結果のみ。 | 戻り値の `item_id` を画面結果判定に使い、キャッシュしない。成功時に商品候補/商品管理カタログキャッシュを破棄する。 |
| `store.reorder_items` | 保存結果のみ。 | 戻り値の `updated_count` を画面結果判定に使い、キャッシュしない。成功時に商品候補/商品管理カタログキャッシュを破棄する。 |
| `store.get_nomination_back_master` | 店舗別指名種別/指名バック設定。`department_id` 単位。 | 現在営業日と同じruntimeキャッシュとして初回成功時に保持する。営業日開始、営業日締め、指名バック設定保存の成功時に破棄する。 |
| `store.save_nomination_back_master` | 保存結果のみ。 | 戻り値の `updated_count` を画面結果判定に使い、キャッシュしない。成功時に指名バック設定キャッシュを破棄する。 |
| `store.get_current_business_day` | 店舗別現在営業日。`department_id` 単位。 | 未締め営業日が取得できた場合だけ保持する。営業日なし、無効な営業日、RPC失敗は保持しない。営業日開始成功時は新しい結果で更新し、営業日締め成功時は破棄する。 |
| `store.open_business_day` | 営業日開始結果。 | 戻り値の営業日を現在営業日キャッシュへ保存する。成功時に指名バック設定キャッシュと当日出勤キャスト候補キャッシュを破棄する。 |
| `store.open_business_day_with_attendance` | 営業日開始結果。 | `store.open_business_day` と同じ。戻り値の営業日を現在営業日キャッシュへ保存し、指名バック設定と当日出勤キャスト候補を破棄する。 |
| `store.close_business_day` | 営業日締め結果。 | 戻り値は画面結果判定に使い、現在営業日キャッシュとしては保持しない。成功時に現在営業日、指名バック設定、当日出勤キャスト候補を破棄する。 |
| `store.get_order_attending_casts` | 店舗別・営業日別の出勤キャスト候補。`department_id` + `business_day_id` 単位。 | 初回成功時に保持する。勤怠保存、退勤情報保存、営業日開始、営業日締めの成功時に対象営業日のキャッシュを破棄する。退勤済みかどうかだけでは破棄しない。 |
| `store.save_business_day_attendance` | 保存結果のみ。 | 戻り値の営業日を画面結果判定に使い、キャッシュしない。成功時に対象営業日の出勤キャスト候補キャッシュを破棄する。 |
| `store.save_business_day_closing_attendance` | 保存結果のみ。 | 戻り値の保存件数を画面結果判定に使い、キャッシュしない。成功時に対象営業日の出勤キャスト候補キャッシュを破棄する。 |
| `store.get_open_slip_count` | 締め可否用の動的件数。 | キャッシュしない。`/Closing` のパネル状態JSON handler、フォーカス復帰、30秒ごとの表示中自動更新、締めPOST検証で最新取得する。 |
| `store.get_business_day_drink_delivery_status` | 締め可否用の動的状態。 | キャッシュしない。酒代入力、`/Closing` のパネル状態JSON handler、フォーカス復帰、30秒ごとの表示中自動更新、締めPOST検証で最新取得する。 |
| `store.save_business_day_drink_delivery_amount` | 保存結果のみ。 | 戻り値の保存金額を画面結果判定に使い、キャッシュしない。締め可否に関わるため次回表示で最新取得する。 |
| `store.get_business_day_closing_attendance` | 締め勤怠の動的一覧。 | キャッシュしない。出退勤保存で変わるため勤怠画面、`/Closing` のパネル状態JSON handler、締めPOST検証で最新取得する。 |
| `store.get_business_day_cast_sales_adjustment_status` | 締め可否用の動的状態。 | キャッシュしない。会計や調整保存で変わるため `/Closing` のパネル状態JSON handler、フォーカス復帰、30秒ごとの表示中自動更新、締めPOST検証で最新取得する。 |
| `store.get_cast_sales_adjustment_slips` | キャスト売上額調整対象の動的一覧。 | キャッシュしない。会計や調整保存で変わるため専用画面表示時に最新取得する。 |
| `store.get_cast_sales_adjustment_detail` | 伝票単位の調整詳細。 | キャッシュしない。対象伝票の調整モーダル表示や保存検証の元データとして最新取得する。 |
| `store.save_cast_sales_adjustment` | 保存結果のみ。 | 戻り値の保存件数を画面結果判定に使い、キャッシュしない。調整状態や対象一覧は次回表示で最新取得する。 |
| `store.get_business_day_slips` | 営業中一覧の動的結果。 | キャッシュしない。営業中トップのJSON handlerから初回表示後、フォーカス復帰、30秒ごとの表示中自動更新で取得する。 |
| `store.get_order_entry_slips` | 注文端末の対象伝票動的結果。 | キャッシュしない。`/Orders` のJSON handlerから初回表示後、フォーカス復帰、30秒ごとの表示中自動更新で取得する。 |
| `store.get_slip_detail` | 伝票詳細の動的結果。 | キャッシュしない。客、指名、注文、調整、カラオケ、会計状態を含むため詳細画面や保存後再表示で最新取得する。 |
| `store.create_slip` | 保存結果のみ。 | 戻り値の `slip_id` を画面結果判定に使い、キャッシュしない。営業日が未作成の場合は事前の `EnsureCurrentAsync` が現在営業日キャッシュを作る。営業中一覧は次回JSON取得で反映する。 |
| `store.add_slip_customers` | 保存結果のみ。 | 戻り値の `inserted_count` を画面結果判定に使い、キャッシュしない。伝票詳細は次回表示で最新取得する。 |
| `store.leave_slip_customer` | 保存結果のみ。 | 戻り値は成功判定に使い、キャッシュしない。伝票詳細と営業中一覧は次回取得で反映する。 |
| `store.update_slip_customer_label` | 保存結果のみ。 | 戻り値は成功判定に使い、キャッシュしない。伝票詳細と営業中一覧は次回取得で反映する。 |
| `store.add_slip_nominations` | 保存結果のみ。 | 戻り値の `inserted_count` を画面結果判定に使い、キャッシュしない。指名種別はキャッシュ済みマスタを検証に使い、実績はRPC側で現在DBマスタからスナップショット保存する。 |
| `store.add_order_lines` | 保存結果のみ。 | 戻り値の `inserted_count` を画面結果判定に使い、キャッシュしない。注文対象伝票や伝票詳細は次回取得で反映する。 |
| `store.void_order_line` | 保存結果のみ。 | 戻り値は成功判定に使い、キャッシュしない。伝票詳細は次回取得で反映する。 |
| `store.save_slip_adjustments` | 保存結果のみ。 | 戻り値の `saved_count` を画面結果判定に使い、キャッシュしない。伝票詳細は次回取得で反映する。 |
| `store.save_karaoke_lines` | 保存結果のみ。 | 戻り値の `saved_count` をAjax結果判定に使い、キャッシュしない。営業中一覧や伝票詳細は次回取得で反映する。 |
| `store.save_order_line_quantities` | 保存結果のみ。 | 戻り値の `saved_count` を画面結果判定に使い、キャッシュしない。通常注文数量、バック実績、伝票詳細、営業中一覧は次回取得で反映する。 |
| `store.confirm_checkout` | 保存結果のみ。 | 戻り値の `checkout_id` と `change_amount` を会計結果判定に使い、キャッシュしない。会計後の営業中一覧、注文対象伝票、締め可否は次回取得で反映する。 |
| `store.cancel_checkout` | 保存結果のみ。 | 戻り値の `checkout_id` を画面結果判定に使い、キャッシュしない。会計取消後の営業中一覧、注文対象伝票、締め可否、キャスト売上額調整状態は次回取得で反映する。客行の退店時刻は変更せず、キャスト売上額調整はリセットする。 |
| `store.get_pending_receipts` | 未処理領収書の動的一覧。 | キャッシュしない。領収書入力や締め可否に直結するため、領収書画面、Driveプレビュー許可判定、`/Closing` のパネル状態JSON handler、締めPOST検証で最新取得する。 |
| `store.quick_enter_receipt` | 保存結果のみ。 | 戻り値を保存成功判定に使い、キャッシュしない。未処理領収書一覧と締め可否は次回取得で反映する。 |
| `store.mark_receipt_scan_mistake` | 保存結果のみ。 | 戻り値を保存成功判定に使い、キャッシュしない。未処理領収書一覧と締め可否は次回取得で反映する。 |

## 9. 実装済み変更と残件

| 項目 | 状態 | SQL/RPC上の整理 |
| --- | --- | --- |
| カラオケ商品化 | 実装済み | `store_item_master.item_type = 'karaoke'` のシステム商品を使い、`store_order_lines` に1伝票1行で集約する。旧 `store_slip_charge_lines.charge_type = 'karaoke'` のアクティブ行は注文行へ移行してvoid化する。 |
| システム商品の注文端末除外 | 実装済み | `store.get_order_items` は標準商品だけを返し、`store.add_order_lines` も標準商品以外を拒否する。カラオケなどのシステム商品は専用RPCで保存する。 |
| カラオケ/指名料金のサービス料対象化 | 実装済み | `store.get_business_day_slips` と `store.confirm_checkout` は、カラオケや指名料金を含む全注文行の小計に20%サービス料を掛ける。システム商品も自由入力調整ではなく注文小計に含める。 |
| 自由入力調整 | 実装済み | `store_slip_charge_lines` は現行運用では `charge_type = 'adjustment'` を扱う。会計額へ直接加減し、商品マスタには登録しない。 |
| 指名料金のシステム商品化 | 実装済み | 指名登録時に `store_item_master.item_type = 'nomination_fee'` のシステム商品を使って `store_order_lines` へ1指名1行を作成する。`store_slip_casts.nomination_price` は入力値の保持と表示に使い、会計集計は指名料金の注文行を商品小計として参照する。 |
| 指名種別別キャストバック | 実装済み | `store_nomination_back_master` で店舗別の指名種別候補と単価を管理し、`store.create_slip` / `store.add_slip_nominations` が `nomination_kind` から基本種別と同伴時刻を解決して `store_slip_cast_backs` へ営業実績を作成する。 |
| 店舗別運用設定 | 実装済み | `department_master.attendance_minute_step`, `cast_sales_amount_basis`, `cast_sales_split_mode` を `store.get_context` で返し、勤怠時刻選択とキャスト売上額調整の初期配分に使う。 |
| 管理者モード締め | 実装済み | `store.close_business_day` は `p_ignore_closing_requirements = true` の場合、未会計伝票、酒代、勤怠、退勤、キャスト売上額調整、未入力領収書の条件検証を無視して営業日を締める。営業日IDと店舗IDの一致確認は維持する。 |
| 当日出勤キャスト候補キャッシュ | 実装済み | RPC定義変更は不要。アプリ側に店舗別・営業日別キャッシュキーを持ち、勤怠保存/退勤情報保存/営業日開始/営業日締め時に破棄する。 |
| 営業中一覧と注文対象伝票の再取得削減 | 実装済み | RPC定義変更は不要。`store.get_business_day_slips` と `store.get_order_entry_slips` はページ用JSON handlerから非同期取得し、初期表示や保存成功POSTをブロックしない。 |
| レシートプリンター正式仕様 | 一部実装 | 80mm紙向けに店舗名、現在時刻、伝票番号、「飲食代として」、会計額、支払い方法、内消費税額を印字し、50,001円以上では収入印紙欄を追加する。再印刷履歴や印刷状態をDB保存する場合は、会計テーブルまたは印刷ログテーブルの追加を検討する。 |
