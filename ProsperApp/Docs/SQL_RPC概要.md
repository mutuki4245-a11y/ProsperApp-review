# ProsperApp SQL/RPC概要

作成日: 2026-07-03
対象: ProsperApp 店舗用アプリ
用途: SQL定義とRPCの俯瞰

## 1. 文書の位置づけ

本書は `Sql/` 配下にある店舗アプリ用SQL定義とRPCの概要をまとめる。実装時に読む入口として使い、最終的な正は各SQLファイルとアプリ側Repository実装で確認する。

DB操作は原則Supabase RPC経由で行う。アプリからのRPC呼び出しは `ISupabaseRpcClient` / `SupabaseRpcClient` に集約し、`prosper-rpc` Edge Function経由で実行する。直接テーブルRESTやREST RPC fallbackは持たない。

現行RPCは `security definer` と `set search_path = public` を前提にし、`Sql/store_rpc/99_grants.sql` で `public`、`anon`、`authenticated`、`service_role` からの直接実行権限を剥奪する。アプリから直接PostgREST RPCを呼べる状態に戻さない。

## 2. SQLファイルの役割

| ファイル | 役割 |
| --- | --- |
| `Sql/store_order_accounting_tables.sql` | 店舗営業、伝票、客行、指名、注文、自由入力調整、会計、締め調整のテーブル定義。RLS有効化、`updated_at` トリガー、主要インデックスを含む。 |
| `Sql/store_settings_functions.sql` | 店舗設定画面用RPC。`get_store_departments()` で有効店舗一覧を返す。 |
| `Sql/store_rpc_functions.sql` | 分割済みRPCファイルの実行順を示す非実行インデックス。実行対象ではない。 |
| `Sql/store_rpc/01_business_day.sql` | 店舗コンテキスト、営業日開始/取得/締め、勤怠、酒代、未会計伝票数を扱う。 |
| `Sql/store_rpc/02_store_masters.sql` | 卓、キャスト、商品、商品管理、指名バック設定、営業中/注文入力向け伝票一覧を扱う。 |
| `Sql/store_rpc/03_slips.sql` | 伝票詳細、客追加/退店、指名追加、自由入力調整、カラオケ商品数量、客名更新、注文取消を扱う。 |
| `Sql/store_rpc/04_orders.sql` | 注文登録と、バックキャスト候補用の当日出勤キャスト取得を扱う。 |
| `Sql/store_rpc/05_checkout.sql` | 会計確定と伝票作成を扱う。 |
| `Sql/store_rpc/06_receipts.sql` | 領収書入力、簡易入力、スキャンミス除外を扱う。 |
| `Sql/store_rpc/07_cast_sales_adjustments.sql` | 締め作業のキャスト売上額調整を扱う。 |
| `Sql/store_rpc/99_grants.sql` | アプリRPCの直接PostgREST実行権限を剥奪する。RPC追加時はこの対象一覧も更新する。 |
| `Sql/store_table_master_seed.sql` | mieu本店の卓番マスタ初期データ。 |
| `Sql/quick_entry_account_master_updates.sql` | 領収書簡易入力UIで使う科目・補助科目の追加更新SQL。実行前に文字化け有無を確認する。 |
| `Sql/agent_schema_reference.sql` | エージェント向けの参照用スキーマ集約ファイル。実行対象ではない。 |

DB反映時の基本順序は以下。

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

## 3. テーブル概要

| 分類 | テーブル | 概要 |
| --- | --- | --- |
| マスタ | `store_table_master` | 店舗ごとの卓番。 |
| マスタ | `cast_master` | キャスト。店舗所属と表示順を持つ。 |
| マスタ | `store_item_category_master` | 商品カテゴリ。 |
| マスタ | `store_item_master` | 商品マスタ。価格、商品種別、キャストバック対象、バック単価、バック種別を持つ。カラオケは `item_type = 'karaoke'` のシステム商品。 |
| マスタ | `store_nomination_back_master` | 店舗別の指名バック設定。本指名、場内指名、同伴のバック単価と有効/無効を持つ。 |
| マスタ | `payment_method_master` | 支払方法マスタ。 |
| 営業日/勤怠 | `store_business_days` | 店舗ごとの営業日。営業開始/締め状態、メモ、酒代などを持つ。 |
| 営業日/勤怠 | `store_cast_attendance` | 営業日ごとのキャスト出退勤。 |
| 伝票 | `store_slips` | 卓単位の伝票ヘッダ。会計額列は持たず、会計額はRPCで都度集計する。 |
| 伝票 | `store_slip_customers` | 伝票内の客行。入退店状態と表示名を持つ。 |
| 伝票 | `store_slip_casts` | 伝票に紐づく指名。指名種別、同伴時刻区分、指名価格を持つ。 |
| 注文/バック | `store_order_lines` | 商品注文行。数量、単価、取消状態を持つ。 |
| 注文/バック | `store_order_line_cast_backs` | 注文行に紐づくバック対象キャスト。 |
| 指名バック | `store_slip_cast_backs` | 指名行に紐づくキャストバック実績。指名登録時点の店舗別マスタ単価をスナップショット保存する。 |
| 自由入力調整 | `store_slip_charge_lines` | 商品マスタとは別枠の伝票調整行。現行運用では `adjustment` を扱い、旧カラオケ別枠行は注文行へ移行してvoid化する。 |
| 会計 | `store_checkouts` | 会計確定結果。会計時点の小計、サービス料、合計を保存する。 |
| 会計 | `store_checkout_payments` | 会計に紐づく支払方法別明細。 |
| 締め調整 | `store_slip_cast_sales_adjustments` | 締め作業で行うキャスト売上額調整。 |

共通設計として、店舗営業系テーブルは `company_id`、`department_id`、必要に応じて `business_day_id` を持つ。主要テーブルはRLSを有効化し、`public.set_updated_at()` による `updated_at` 更新トリガーを持つ。検索頻度が高い有効マスタ、営業日、営業中伝票、伝票明細、会計、締め調整には用途別インデックスを置く。

## 4. 会計額の扱い

`store_slips` に会計額を保持する列はない。営業中一覧の `accounting_amount` は `get_business_day_slips` が返す表示用の集計値であり、永続化された確定額ではない。

営業中の会計額は以下を元に集計する。

- 有効な `store_order_lines` の注文小計
- 注文小計に対する20%サービス料
- 有効な `store_slip_casts` の `nomination_price`
- 有効な `store_slip_charge_lines` のうち自由入力調整の合計

カラオケは `store_item_master.item_type = 'karaoke'` の商品として扱い、`store_order_lines` に1伝票1行で集約する。単価は1回200円固定で、注文小計に含まれるためサービス料20%の対象になる。`ordered_at` は入店時刻に合わせ、異なるタイミングで追加したカラオケも同一伝票では数量だけを更新する。

会計確定時は `confirm_store_checkout` が注文、指名、自由入力調整を再集計し、支払合計と照合したうえで `store_checkouts.subtotal_amount`、`store_checkouts.service_tax_amount`、`store_checkouts.total_amount` と `store_checkout_payments` を保存する。営業中一覧の表示額を確定額として信用しない。

## 5. RPC概要

### 店舗設定

| RPC | 主な用途 |
| --- | --- |
| `get_store_departments` | 有効な店舗一覧を返す。 |

### 店舗コンテキスト・営業日

| RPC | 主な用途 |
| --- | --- |
| `get_store_context` | 店舗IDから店舗コンテキストを返す。 |
| `get_current_business_day` | 未締めの現在営業日を返す。締めるまでキャッシュ対象。 |
| `open_business_day` | 営業日を開始する。 |
| `open_business_day_with_attendance` | 営業日開始と勤怠一括登録を行う。 |
| `save_business_day_attendance` | 営業中の勤怠入力を保存する。 |
| `get_business_day_closing_attendance` | 締め作業用の勤怠一覧を返す。 |
| `save_business_day_closing_attendance` | 締め作業用の勤怠修正を保存する。 |
| `get_open_slip_count` | 未会計伝票数を返す。 |
| `get_business_day_drink_delivery_status` | 酒代入力状態を返す。 |
| `save_business_day_drink_delivery_amount` | 酒代を保存する。 |
| `close_business_day` | 営業日を締める。 |

### マスタ・一覧

| RPC | 主な用途 |
| --- | --- |
| `get_store_tables` | 卓番候補を返す。 |
| `get_store_casts` | キャスト候補を返す。ヘルプ対応のため同一会社内の有効店舗所属キャストも含む。 |
| `get_store_cast_admin_list` | キャスト管理画面用一覧を返す。 |
| `create_store_cast` | キャストを作成する。 |
| `delete_store_cast` | キャストを削除または無効化する。 |
| `get_business_day_slips` | 営業中画面向けの伝票一覧と会計表示額を返す。 |
| `get_order_entry_slips` | `/Orders` 向けの注文入力対象伝票一覧を返す。 |
| `get_store_order_items` | 注文入力用の商品一覧を返す。 |
| `get_store_item_admin_catalog` | 商品管理画面用のカテゴリ/商品一覧を返す。 |
| `get_store_nomination_back_master` | 指名バック設定画面用に、本指名、場内指名、同伴の店舗別設定を返す。 |
| `save_store_nomination_back_master` | 指名バック設定を店舗別に保存する。 |
| `upsert_store_item_category` | 商品カテゴリを作成/更新する。 |
| `upsert_store_item` | 商品を作成/更新する。 |
| `delete_store_item` | 商品を削除または無効化する。 |
| `reorder_store_items` | 商品表示順を更新する。 |

### 伝票

| RPC | 主な用途 |
| --- | --- |
| `get_store_slip_detail` | 伝票詳細、客行、指名、注文、自由入力調整、会計候補を返す。カラオケは注文行の `item_type = 'karaoke'` で判定する。 |
| `create_store_slip` | 伝票を作成する。初期指名がある場合は指名バック設定から `store_slip_cast_backs` も作成する。 |
| `add_store_slip_customers` | 既存伝票へ客行を追加する。 |
| `add_store_slip_nominations` | 既存伝票へ指名を追加する。指名バック設定が有効かつ0円より大きい場合は `store_slip_cast_backs` を作成する。 |
| `leave_store_slip_customer` | 客行を退店扱いにする。 |
| `save_store_slip_adjustments` | 自由入力の会計調整行を保存する。 |
| `save_store_karaoke_lines` | 営業日内のカラオケ商品数量を伝票単位のJSON payloadで保存する。同一伝票のカラオケ注文行は1行に集約する。 |
| `update_store_slip_customer_label` | 客行の表示名を更新する。 |
| `void_store_order_line` | 注文行を取消する。 |

### 注文

| RPC | 主な用途 |
| --- | --- |
| `get_order_attending_casts` | 当日出勤キャストを返す。退勤済みも候補に残す。 |
| `add_store_order_lines` | 注文行とバック対象キャストを登録する。`p_order_lines` に伝票IDを含められるため、`/Orders` では複数卓のキューをまとめて登録できる。 |

### 会計

| RPC | 主な用途 |
| --- | --- |
| `confirm_store_checkout` | 会計額を再計算し、支払合計を検証して会計確定する。 |

### 領収書入力

| RPC | 主な用途 |
| --- | --- |
| `get_pending_receipts` | 未処理領収書一覧を返す。 |
| `quick_enter_receipt` | 領収書を簡易入力する。DocManagement連携用payload引数を受け取る。 |
| `mark_receipt_scan_mistake` | 領収書をスキャンミスとして除外する。 |

### 締め作業のキャスト売上額調整

| RPC | 主な用途 |
| --- | --- |
| `get_business_day_cast_sales_adjustment_status` | 締め作業画面に表示するキャスト売上額調整状態を返す。 |
| `get_cast_sales_adjustment_slips` | 調整対象伝票一覧を返す。 |
| `get_cast_sales_adjustment_detail` | 伝票単位の調整詳細を返す。 |
| `save_cast_sales_adjustment` | キャスト売上額調整を保存する。 |

## 6. 呼び出し経路

Razor PageのPageModelはRepositoryを呼び、Repositoryが `ISupabaseRpcClient` を通じてRPC名とpayloadを `prosper-rpc` Edge Functionへ渡す。Edge Function側で許可済みRPCだけを実行し、Repositoryは戻り値のJSON配列またはスカラーをDTOへ変換する。

設定キーやsecretの値はリポジトリに置かない。Azure環境変数とSupabase Edge Function Secretsの名称・値は運用環境で一致させる。

## 7. RPC更新時の注意

RPCを追加/変更するときは、以下を同じタスク内で揃える。

1. 対象の `Sql/store_rpc/*.sql` または `Sql/store_settings_functions.sql`
2. `Sql/store_rpc/99_grants.sql` の対象RPC一覧
3. `prosper-rpc` Edge Function側の許可RPC一覧
4. アプリ側Repository、DTO、JSONパース処理
5. 必要に応じて `HANDOFF.md`、`Docs/システム仕様書.md`、本書

一覧RPCは対象営業日や対象伝票を先に絞ってから関連行を集計する。特に `get_business_day_slips` と `get_cast_sales_adjustment_slips` は、全期間の客、指名、注文、自由入力明細を集計してから最後に絞る形へ戻さない。

アプリ側では、店舗一覧、店舗コンテキスト、卓、キャストマスタ候補、商品候補、商品管理カタログ、キャスト管理一覧、指名バック設定、現在営業日を `IMemoryCache` の対象として扱う。RPC失敗や設定未完了の結果はキャッシュしない。商品/カテゴリ保存、商品削除、商品並び順保存、キャスト登録/削除、指名バック設定保存、営業日開始、営業日締めなどの破棄契機は `HANDOFF.md` の重要方針に従う。

`get_order_attending_casts` は現行実装では都度取得する。営業日単位キャッシュを導入する場合は、勤怠保存、営業日開始、営業日締めで破棄する実装を同時に入れる。

## 8. 実装済み変更と残件

| 項目 | 状態 | SQL/RPC上の整理 |
| --- | --- | --- |
| カラオケ商品化 | 実装済み | `store_item_master.item_type = 'karaoke'` のシステム商品を使い、`store_order_lines` に1伝票1行で集約する。旧 `store_slip_charge_lines.charge_type = 'karaoke'` のアクティブ行は注文行へ移行してvoid化する。 |
| カラオケのサービス料対象化 | 実装済み | `get_business_day_slips` と `confirm_store_checkout` は、カラオケを含む注文小計に20%サービス料を掛ける。カラオケは自由入力調整ではない。 |
| 自由入力調整 | 実装済み | `store_slip_charge_lines` は現行運用では `charge_type = 'adjustment'` を扱う。会計額へ直接加減し、商品マスタには登録しない。 |
| 指名価格 | 実装済み | `store_slip_casts.nomination_price` を会計額へ加算する。 |
| 指名種別別キャストバック | 実装済み | `store_nomination_back_master` で店舗別単価を管理し、`create_store_slip` / `add_store_slip_nominations` が指名登録時に `store_slip_cast_backs` へ営業実績を作成する。 |
| 当日出勤キャスト候補キャッシュ | 検討候補 | RPC定義変更は不要。アプリ側に営業日単位キャッシュキーと、勤怠保存/営業日開始/営業日締め時の破棄処理を追加するか検討する。 |
| レシートプリンター正式仕様 | 後続仕様 | SQL/RPC変更は現時点で不要。再印刷履歴や印刷状態をDB保存する場合は、会計テーブルまたは印刷ログテーブルの追加を検討する。 |
