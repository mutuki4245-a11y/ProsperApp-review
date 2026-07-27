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
| `Sql/store_rpc/08_checkout_ready.sql` | 会計伝票、会計準備、支払確定、領収書印刷データを扱う。 |
| `Sql/store_rpc/09_business_home_snapshot.sql` | 営業中トップ向けの営業日全伝票スナップショットと、客・指名・自由明細を一操作ずつ保存して同スナップショットを返すRPCを扱う。 |
| `Sql/store_rpc/99_grants.sql` | アプリRPCの直接PostgREST実行権限を剥奪する。RPC追加時はこの対象一覧も更新する。 |
| `Sql/store_table_master_seed.sql` | mieu本店の卓番マスタ初期データ。 |
| `Sql/quick_entry_account_master_updates.sql` | 領収書簡易入力UIで使う科目・補助科目の追加更新SQL。会計マスタは `accounting` schema、会社マスタは `public` schema を完全修飾し、有効会社だけを対象にする。会計データを変更するため、対象会社・補助科目・マップ件数を確認してから実行する。 |
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
11. `Sql/store_rpc/08_checkout_ready.sql`
12. `Sql/store_rpc/09_business_home_snapshot.sql`
13. `Sql/store_rpc/99_grants.sql`
14. 必要に応じて `Sql/store_table_master_seed.sql`
15. 必要に応じて `Sql/quick_entry_account_master_updates.sql`

## 2.1 会計フロー第1段階の実装状況（2026-07-20）

以下はアプリ、SQL、`prosper-rpc` Edge Function allowlistの**ソース実装済み**であり、リモートDBへのSQL適用とEdge Functionデプロイは未実施である。リモート環境の旧 `store.confirm_checkout` 契約を本書の現在契約として扱わない。

- `store.issue_checkout_statement(p_department_id, p_slip_id, p_closed_at)` は `open -> checkout_ready`、退店時刻固定、未退店客の `left_at_source = 'accounting_slip'` 補完を行い、会計伝票の `print_data` と会計確認用の `review_data` を返す。
- `store.get_checkout_statement_print_data(p_department_id, p_slip_id)` は `checkout_ready` の会計伝票を再生成し、同じ `print_data` と `review_data` を返す。
- `store.release_checkout_ready(p_department_id, p_slip_id)` は `checkout_ready -> open` とし、`closed_at` と `accounting_slip` 由来の退店時刻だけを戻す。
- `store.confirm_checkout(p_department_id, p_slip_id, p_payments, p_received_amount)` は `checkout_ready` だけを確定し、`checkout_id`、釣銭、初回領収書用 `print_data` を返す。旧 `p_closed_at` と `p_confirmed_snapshot` は受け取らない。
- `store.get_checkout_receipt_print_data(p_department_id, p_slip_id)` は有効な `checked_out` 会計から `checkout_id` と領収書 `print_data` を1回で返す。
- これら5 RPCは `prosper-rpc` のallowlistへ追加・更新済みで、公開しない `store.build_checkout_statement_data`、`store.build_checkout_review_data`、`store.build_checkout_receipt_data` は直接実行権限を持たない。

会計伝票の端末内印刷状態と領収書の初回印刷成功記録はlocalStorageだけで管理し、DBの伝票状態・印刷ジョブ・印刷履歴へ保存しない。領収書の `再試行` 表記は、同じ端末で初回印刷成功記録があり、店員が明示的にもう一度出力する場合だけである。

## 3. テーブル概要

| 分類 | テーブル | 概要 |
| --- | --- | --- |
| 既存マスタ | `department_master` | 店舗マスタ。店舗別運用設定として勤怠時刻刻み、キャスト売上額調整の売上額基準、売上額人数割を持つ。 |
| マスタ | `store_table_master` | 店舗ごとの卓番。`table_category_no`（0〜9）でカテゴリ順を持つ。 |
| マスタ | `cast_master` | キャスト。店舗所属、表示順、任意入力の `drink_memo`（30文字以内）を持つ。 |
| マスタ | `store_item_category_master` | 商品カテゴリ。 |
| マスタ | `store_item_master` | 商品マスタ。価格、商品種別、キャストバック対象、バック単価、バック種別を持つ。カラオケは `item_type = 'karaoke'`、指名料金は `item_type = 'nomination_fee'` のシステム商品。 |
| マスタ | `store_nomination_back_master` | 店舗別の指名種別と指名バック設定。`nomination_kind`、基本種別、表示名、同伴時刻、バック単価、有効/無効を持つ。 |
| マスタ | `payment_method_master` | 支払方法マスタ。今回の会計改修では現金、CAT、PAYPAYの3種類だけを使い、運用上の追加・無効化は別タスクとする。 |
| 営業日/勤怠 | `store_business_days` | 店舗ごとの営業日。営業開始/締め状態、メモ、酒代などを持つ。 |
| 営業日/勤怠 | `store_cast_attendance` | 営業日ごとのキャスト出退勤。 |
| 伝票 | `store_slips` | 卓単位の伝票ヘッダ。会計額列は持たず、会計額はRPCで都度集計する。会計伝票出力後の専用ステータス `checkout_ready` を含む現在定義はソース実装済み（未適用）。 |
| 伝票 | `store_slip_customers` | 伝票内の客行。入退店状態と表示名、退店時刻の由来 `left_at_source` を持つ。個別退店登録は `manual`、会計伝票出力による補完は `accounting_slip` とする現在定義はソース実装済み（未適用）。 |
| 伝票 | `store_slip_casts` | 伝票に紐づく指名。指名種別、同伴時刻区分、指名料金の選択額を持つ。 |
| 注文/バック | `store_order_lines` | 商品注文行。数量、単価、取消状態を持つ。指名料金は `source_type = 'nomination_fee'` / `source_id = slip_cast_id` で指名行に紐づく。 |
| 注文/バック | `store_order_line_cast_backs` | 注文行に紐づくバック対象キャスト。通常の商品注文バックはドリンクバック、対象キャストが当該伝票の指名キャストだった場合は担当バックとして扱う。 |
| 指名バック | `store_slip_cast_backs` | 指名行に紐づくキャストバック実績。指名登録時点の店舗別マスタ単価をスナップショット保存する。 |
| 自由入力調整 | `store_slip_charge_lines` | 商品マスタとは別枠の伝票調整行。現行運用では `adjustment` を扱い、旧カラオケ別枠行は注文行へ移行してvoid化する。 |
| 会計 | `store_checkouts` | 会計確定結果。会計時点の小計、サービス料、合計、`issuer_snapshot` を保存する。サービス料列は `service_charge_amount` とし、消費税を意味する名前を使わない現在定義はソース実装済み（未適用）。 |
| 会計 | `store_checkout_payments` | 会計に紐づく支払方法別明細。 |
| 発行者 | `company_master` / `department_master` | 法人名・適格請求書登録番号は `company_master`、店舗表示名・住所・電話番号・ロゴは `department_master` に持たせ、適格簡易請求書の発行元として返す。列追加とサンプルseedはソース実装済み（未適用）。 |
| 締め調整 | `store_slip_cast_sales_adjustments` | 締め作業で行うキャスト売上額調整。 |

共通設計として、店舗営業系テーブルは `company_id`、`department_id`、必要に応じて `business_day_id` を持つ。主要テーブルはRLSを有効化し、`public.set_updated_at()` による `updated_at` 更新トリガーを持つ。検索頻度が高い有効マスタ、営業日、営業中伝票、伝票明細、会計、締め調整には用途別インデックスを置く。

## 4. 会計額の扱い

`store_slips` に会計額を保持する列はない。営業中一覧の `accounting_amount` は `store.get_business_day_slips` が返す表示用の集計値であり、永続化された確定額ではない。

現行ソースでは、`store.issue_checkout_statement` で会計伝票出力時に退店時刻を選択して `store_slips.closed_at` と未退店客行の退店時刻を固定し、`store_slips.status` を `checkout_ready` へ変更する。`checkout_ready` への変更は物理プリンターの成功ではなく、サーバー側の会計伝票出力RPC成功を基準にする。プリンター失敗時もDB状態は戻さず、`checkout_ready` のまま再印刷または会計準備解除で対応する。`checkout_ready` は `checked_out` と同じように注文追加、指名追加、自由入力明細変更など会計内容が変わるRPCの対象外にする。会計準備解除RPCは `store.release_checkout_ready` とし、`store_slips.status = 'checkout_ready'` の伝票だけを対象にし、`checked_out` 済み伝票には使わない。`checked_out` を戻す場合は `store.cancel_checkout` を使う。解除時は通常の営業中状態へ戻し、`store_slips.closed_at = null` に戻したうえで、`left_at_source = 'accounting_slip'` の退店時刻だけを未確定に戻す。`left_at_source = 'manual'` の個別退店登録は残す。リモートDB適用は未実施である。

`checkout_ready` は締め条件や未会計数では会計済みとして扱わない。`store.get_open_slip_count` や締め前検証では `open` と `checkout_ready` を未会計側に含める。一方で営業中一覧や伝票詳細では通常編集中の `open` とは分け、「会計準備中」として編集不可、再印刷、会計準備解除、会計確定へ進む状態を返す。

顧客名は注文端末と締め時のキャスト売上額調整一覧で使う伝票内の内部識別であり、領収書宛名には使わない。`store.update_slip_customer_label` は後続では `open` の伝票だけを更新対象にし、`checkout_ready` と `checked_out` では確定値として更新を拒否する。

`store_slip_customers.left_at_source` は、`left_at` が `null` の場合は `null`、個別退店登録で `left_at` を設定した場合は `manual`、会計伝票出力で未退店客へ退店時刻を補完した場合は `accounting_slip` とする。`store.release_checkout_ready` は `checkout_ready` 専用とし、`store_slips.closed_at = null` に戻し、客行は `accounting_slip` 由来だけ `left_at = null, left_at_source = null` に戻す。

会計伝票出力は、営業中一覧の表示結果をそのまま確定資料にせず、対象伝票の最新保存状態を1RPCで取得してから退店時刻選択と会計準備中化を行う。通常表示の10秒更新で取りこぼし得る直前注文は、この直前取得で反映する。直前取得で一覧表示との差分があっただけではエラーにせず、画面側は最新状態を表示へ反映して出力操作を継続する。

`store.issue_checkout_statement` は、対象伝票の最新状態取得/検証、選択された退店時刻の検証、`store_slips.closed_at` 固定、`store_slips.status = 'checkout_ready'` 更新、未退店客行への `left_at` と `left_at_source = 'accounting_slip'` 補完、会計伝票の印刷用データ返却までを1RPCで完結させる。状態更新と印刷用データ生成を別RPCに分けず、返却する印刷用データは同一トランザクションで固定したDB状態を元にする。

`store.issue_checkout_statement` と `store.get_checkout_statement_print_data` の返却値は、`slip_id` と `print_data jsonb` を中心にする。`slip_id` は端末内の対象特定にだけ使い、`print_data` は `schema_version = 'checkout-statement-v1'` を持つ会計伝票印字用JSONとする。店舗表示名、明細、自由入力明細、金額サマリー、入店・退店時刻、卓番、客数など既決定の印字項目だけを入れ、`checkout_id`、`slip_id`、`slip_no`、顧客名、指名キャスト名・種別、バック対象キャスト、注文時刻一覧は印字データの表示項目に含めない。客数は会計伝票発行時点の `store_slip_customers.status <> 'cancelled'` の件数とし、途中退店客を含める。公開RPCの列を明細単位で増やさず、アプリ側Repositoryは `print_data` を会計伝票印刷モデルへ変換する。


営業中端末は店舗1台で、会計準備中の伝票を別端末から変更しない運用を前提にする。`store.issue_checkout_statement` が返す `print_data` は、物理印刷開始前に同一ブラウザの会計伝票用 `localStorage` キューへ保存し、成功後に直ちに物理印刷を開始する。同じ端末での印刷失敗・再印刷は保存済みデータを使うため、通常は追加RPCを呼ばない。会計準備中は自由に再印刷でき、紙面に `再印刷` などの表記を付けず、印刷履歴も保存しない。キュー項目は `slip_id` を識別子にし、`印刷待ち`、`印刷中`、`印刷失敗`、`印刷済み` を表す。端末内の印刷状態は会計確定の条件にせず、会計準備中のモーダルで再印刷、会計準備解除、決済への移行を選べる。記録は会計確定RPCの成功応答時に削除し、会計確定に失敗した場合は残して再利用できるようにする。

ローカルデータが消失、破損、または別ブラウザ利用で存在しない場合だけ、`store.get_checkout_statement_print_data(p_department_id, p_slip_id)` を復旧用に呼ぶ。専用の印刷スナップショットテーブルは作らず、`会計準備中` でロックされた現在DB状態から再生成し、取得後はローカルキューへ保存する。生成元は `store_slips.closed_at`、`store_slip_customers`、`store_order_lines`、`store_slip_charge_lines`、会計準備中ステータスを基本にする。再印刷では `closed_at`、客行の退店時刻、伝票ステータスを変更しない。DB上の `checkout_ready` は状態と会計確定の正であり、localStorageは再印刷のレスポンス改善用データ保持に限定する。営業中一覧の定期更新では印刷用JSONを返さない。印字済み内容そのものの監査や印刷履歴が必要になった場合だけ、後続でスナップショットテーブルまたは印刷ログテーブルを検討する。

領収書の最初の印刷要求は `store.confirm_checkout` の成功応答後だけに作る。成功応答は初回印刷用の領収書 `print_data` を含め、その値を直接印刷する。`checkout_id`、`slip_id`、`slip_no` は対象特定の内部識別子であり、領収書の表示項目と紙面へ含めない。直後の印刷失敗を同一ブラウザのlocalStorageで通知・再試行してよいが、`pending`、`sent`、`failed` をDBの伝票状態、印刷ジョブ、印刷履歴として保存しない。`checked_out` の伝票は営業中トップからいつでも手動再発行でき、確定済み伝票と会計・決済の保存データから印刷データを読み取り専用で再生成する。紙面に `再試行` を印字するのは、同じ端末に初回印刷成功記録があり、店員が明示的に再出力する場合だけである。会計取消済み伝票は対象外とする。宛名は任意入力で会計データに保存しない。本番化までに、適格簡易請求書の必要事項である発行者名・登録番号、取引日、取引内容、税率ごとの対価額、税率ごとの消費税額または適用税率を、確定済み会計・決済・明細と会計確定時の発行者スナップショットから再現できるようにする。商品、自由入力明細、サービス料を含む課税対象は10%固定であり、税込会計額を10%対象額として扱う。複数税率の税区分・集計は別仕様とする。

後続の `store.get_checkout_receipt_print_data(p_department_id, p_slip_id)` は、`checked_out` かつ有効な確定会計がある伝票だけを対象に、確定済み伝票、会計、決済、明細、会計確定時の発行者スナップショットを領収書用 `print_data jsonb` として返す手動再発行用の読み取り専用RPCとする。応答は端末内の初回成功記録キー用に `checkout_id` と `print_data` を別フィールドで返し、IDを紙面用データへ混ぜない。`store.confirm_checkout` の初回印刷用 `print_data` と同じ内部SQLヘルパーを使う。通常の `store.get_slip_detail` へ会計・決済情報を追加せず、領収書画面が複数RPCを呼んだり、C#やJavaScriptで領収書の構成を再実装したりしないようにする。

会計伝票印字データの生成は、公開RPCごとに再実装しない。後続実装では `store.build_checkout_statement_data(...)` のような内部用SQL関数を作り、`store.issue_checkout_statement` と `store.get_checkout_statement_print_data` は同じヘルパー結果を使う。この内部関数はアプリから直接呼ばせず、`99_grants.sql` でもexecute権限を付与しない。

営業中の会計額は以下を元に集計する。

- 有効な `store_order_lines` 全体の注文小計。標準商品、カラオケ、指名料金などのシステム商品を含む。
- 注文小計に対する20%サービス料
- 有効な `store_slip_charge_lines` のうち自由入力調整の合計

カラオケは `store_item_master.item_type = 'karaoke'` の商品として扱い、`store_order_lines` に1伝票1行で集約する。単価は1回200円固定で、注文小計に含まれるためサービス料20%の対象になる。`ordered_at` は入店時刻に合わせ、異なるタイミングで追加したカラオケも同一伝票では数量だけを更新する。

指名料金は `store_item_master.item_type = 'nomination_fee'` のシステム商品として扱い、指名登録時に `store_order_lines` へ1指名1行で自動追加する。商品注文端末からは注文できず、通常注文の数量訂正・削除対象にも含めない。カラオケと指名料金を含むシステム商品は、会計では標準商品と同じく商品小計とサービス料20%の対象にする。

用語は、会計額へ加算する料金を `指名料金`、指名時にキャストへ支払うバックを `指名バック`、商品注文時にキャストへ支払う通常バックを `ドリンクバック`、商品注文バック対象が当該伝票の指名キャストだった場合のバックを `担当バック` と呼び分ける。

現行ソースの会計確定では `store.confirm_checkout` が注文、指名、自由入力調整を再集計し、支払合計と照合したうえで `store_checkouts.subtotal_amount`、`store_checkouts.service_charge_amount`、`store_checkouts.total_amount`、`issuer_snapshot` と `store_checkout_payments` を保存する。サービス料は20%の店舗料金であり消費税ではない。旧列・旧JSONキーの互換はテスト段階では作らない。営業中一覧の表示額を確定額として信用しない。

現行ソースでは、`store.confirm_checkout` は `store_slips.status = 'checkout_ready'` の伝票だけを会計確定対象にする。`open` 伝票からの直接会計確定は禁止し、状態が `checkout_ready` でない場合は会計確定せず、画面側は会計伝票出力または最新状態の再確認へ戻す。

現行ソースでは、`store.confirm_checkout` は `p_closed_at` と `p_confirmed_snapshot` を受け取らない。退店時刻は `store.issue_checkout_statement` で検証して `store_slips.closed_at` に固定済みの値だけを使い、会計確定時に再入力または更新しない。`store.confirm_checkout` は `checkout_ready` と `store_slips.closed_at` が設定済みであることを確認する。

現行ソースでは、再集計した会計額が0円の場合、`store.confirm_checkout` は支払方法明細なしで会計確定する。0円会計では `store_checkouts.total_amount = 0` を保存し、現金0円やカード0円の `store_checkout_payments` 行は作成しない。画面、領収書、会計結果の支払方法表示は `請求なし 0円` とする。

0円以外の会計では、画面側が支払方法別入力額の合計一致を確定時に確認する。決済方法追加時の初回残額入力はUI補助であり、RPCは入力額を推測または自動補正しない。`store.confirm_checkout` は受け取った支払明細合計が再集計した会計額と一致することを検証する。

会計確定前に追加の事前確認RPCは増やさない。現行ソースでは、会計伝票出力で固定した `checkout_ready` と `store_slips.closed_at` を前提に、`store.confirm_checkout` が支払方法、預り金、釣銭、支払合計を検証する。

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
| `store.get_tables` | 卓番候補をカテゴリ番号、既存の表示順、卓番順で返す。 |
| `store.get_casts` | キャスト候補を返す。ヘルプ対応のため会社を跨いだ全有効店舗所属キャストを含む。 |
| `store.get_casts_admin` | キャスト管理画面用に、現在店舗所属キャストだけをドリンクメモとともに返す。 |
| `store.create_cast` | キャストを作成する。任意のドリンクメモは空欄なら `null` として保存する。 |
| `store.update_cast_drink_memo` | 現在店舗の有効キャストに限り、任意のドリンクメモを更新する。 |
| `store.delete_cast` | キャストを論理削除する。`cast_master.status = 'inactive'`、`is_active = false` に更新する。 |
| `store.get_business_day_slips` | 営業中画面向けの伝票一覧、客・指名、注文件数・注文合計、自由入力明細合計、会計表示額を返す。 |
| `store.get_order_entry_slips` | `/Orders` 向けの注文入力対象伝票一覧を返す。卓番、客数、客名、指名キャストを含み、注文端末の卓番選択には客名と指名キャストを表示する。 |
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
| `store.get_slip_detail` | 伝票詳細、客行、指名、注文、自由入力調整、会計候補を返す。カラオケは注文行の `item_type = 'karaoke'`、指名料金は `item_type = 'nomination_fee'` で判定する。注文バック実績がある注文行は `order_back_cast_*` 列でバック対象キャスト名も返す。 |
| `store.create_slip` | 伝票を作成する。初期指名がある場合は指名料金のシステム注文行と、指名バック設定に基づく `store_slip_cast_backs` を作成する。 |
| `store.add_slip_customers` | 既存伝票へ客行を追加する。 |
| `store.add_slip_nominations` | 既存伝票へ指名を追加する。指名料金のシステム注文行と、指名バック設定が有効かつ0円より大きい場合は `store_slip_cast_backs` を作成する。 |
| `store.leave_slip_customer` | 客行を退店扱いにする。 |
| `store.add_slip_adjustment` | 伝票詳細の自由入力明細モーダルから調整行を1件追加する。 |
| `store.save_karaoke_lines` | 営業中トップの遷移時保存で、営業日内のカラオケ商品数量を伝票単位のJSON payloadで保存する。同一伝票のカラオケ注文行は1行に集約する。 |
| `store.save_order_line_quantities` | 伝票詳細の訂正モードから通常注文行の数量を保存する。数量0は対象注文行と紐づくバック実績を取消扱いにする。 |
| `store.update_slip_customer_label` | 客行の表示名を更新する。現行は `open` と `checked_out` を許可するが、後続では `open` だけを許可し、会計準備中・会計済み後の客名は確定値として更新しない。 |
| `store.void_order_line` | 注文行を取消する。 |

### 注文

| RPC | 主な用途 |
| --- | --- |
| `store.get_order_attending_casts` | 当日出勤キャストをドリンクメモとともに返す。退勤済みも候補に残す。メモはバック対象選択時の表示だけに使い、注文実績へ保存しない。 |
| `store.add_order_lines` | 注文行とバック対象キャストを登録する。`p_order_lines` に伝票IDを含められるため、`/Orders` では複数卓のキューをまとめて登録できる。登録時に対象伝票が選択店舗のopen伝票であることを確認し、登録できる商品は標準商品だけに限定する。システム商品は拒否する。 |

### 会計

| RPC | 主な用途 |
| --- | --- |
| `store.confirm_checkout` | 会計額を再計算し、`checkout_ready` 伝票だけを支払合計検証のうえ確定する。`p_closed_at` と `p_confirmed_snapshot` は受け取らず、固定済みの `store_slips.closed_at` を使う。成功応答は会計ID、釣銭、初回領収書用 `print_data jsonb` を返す。0円会計は支払方法明細なしで確定する。 |
| `store.cancel_checkout` | 開いている営業日の会計済み伝票を営業中へ戻す。会計と支払明細は `cancelled` にし、客行の退店状態と退店時刻は変更しない。会計に紐づくキャスト売上額調整は削除してリセットする。 |
| `store.issue_checkout_statement` | ソース実装済み（未適用）。会計伝票をサーバー側で発行し、`checkout_ready` へ状態更新したうえで `print_data jsonb` と `review_data jsonb` を返す。物理印刷成功を意味しない。 |
| `store.get_checkout_statement_print_data` | ソース実装済み（未適用）。同一端末のlocalStorageが欠落・破損した場合や別ブラウザでの復旧に使う読み取り専用RPC。`checkout_ready` の現在DB状態から `print_data jsonb` と `review_data jsonb` を返す。 |
| `store.get_checkout_receipt_print_data` | ソース実装済み（未適用）。`checked_out` の確定済み伝票を対象に、会計、決済、会計確定時の発行者スナップショットを領収書用 `print_data jsonb` として返す読み取り専用RPC。会計取消済み伝票は返さない。 |
| `store.release_checkout_ready` | ソース実装済み（未適用）。`checkout_ready` の伝票だけを通常営業へ戻す。会計済みを戻す場合は `store.cancel_checkout` を使う。 |
| `store.build_checkout_statement_data` | ソース実装済み（未適用）。会計伝票印字データを生成する内部用SQL関数。公開RPCではなく、アプリから直接呼ばせない。 |

領収書の初回印刷が失敗した場合、同一ブラウザの端末内通知から行う最初の再試行は初回印刷として扱い、紙面に追加表記を出さない。会計確定成功後に成功応答が端末へ届かず初回印刷要求を作れなかった場合も、後で営業中トップから行う最初の物理印刷は初回印刷として扱う。`doPrint` 成功時だけ同じ会計端末のlocalStorageへ `checkout_id` ごとの初回成功記録を残し、記録ありで店員が明示的にもう一度出力する場合だけ `再試行` を紙面へ印字する。成功記録がない、または端末内データが消失した場合は初回印刷として扱い、会計取消時は成功記録と失敗通知を削除する。手動再発行は操作上の呼称であり、紙面に `再発行` は印字しない。この判定は端末内の出力操作だけで行い、RPC、DBの印刷状態、印刷履歴を追加しない。

`store.get_checkout_receipt_print_data` は、確定済み明細をサーバー側で検証・税率別に集計するが、顧客向け紙面に必要のない明細配列と内部IDは `print_data` の表示項目として返さない。取引内容は `ご飲食代として` の1行とし、返却値は取引内容、税率別集計、決済、発行者情報を中心にする。確定明細は既存の会計記録から再現可能に保つ。

当面の `print_data` の税率別集計は10%だけであり、`taxable_amount_including_tax = total_amount`、`consumption_tax_amount = round(total_amount * 10 / 110)` を返す。紙面には `10%対象 ¥<税込額>（内消費税 ¥<税額>）` として印字する。税額の再計算をC#やJavaScriptへ持ち込まない。

`print_data.payments` は方法名と決済額の配列として返し、紙面は方法ごとに別行で印字する。`CAT` の表示名は `クレジット` とする。0円会計は決済配列を空にし、紙面で `請求なし 0円` と表示する。現金の預り金と釣銭は会計処理の確認情報として保持するが、`print_data` には返さない。C#やJavaScriptは方法名を連結したり決済額を再配分したりしない。

営業日締め後を含む確定済み伝票の検索・再試行一覧は将来候補であり、今回の後続実装範囲では検索RPCを追加しない。入口画面、実装時期、検索RPCの契約は未決定である。実装時は営業日、会計確定日時、卓番、会計額を返し、取消済み伝票を除外する。`checkout_id` と `slip_no` は内部管理に留める。

最初の会計SQL/RPC切替では、発行者マスタ列の追加、画面実装前のサンプル値のseed、`store_checkouts.issuer_snapshot` の追加、`store.confirm_checkout` による保存と初回領収書用 `print_data` の返却を同じ変更単位で行う。発行者マスタの編集画面だけを後続タスクに分ける。`store.confirm_checkout` は、会計確定と同じトランザクションで `store_checkouts.issuer_snapshot jsonb` を保存し、同じ確定済みデータを元に初回領収書用 `print_data` を返す。領収書の日付は `store_checkouts.checkout_at` を店舗時刻へ変換して返し、`store_slips.closed_at` や営業日を流用しない。`issuer_snapshot.schema_version` は `issuer-v1` とし、法人名、適格請求書登録番号、店舗表示名、住所、電話番号、ロゴを含める。発行者項目が欠けていてもRPCは会計確定を拒否せず、その時点で得られる値を保存する。値の元は法人名・登録番号を `company_master`、店舗表示名・住所・電話番号・ロゴを `department_master` とする。画面実装前のサンプル値も同マスタへseedし、`ReceiptPrinter` 設定を発行者情報の元にしない。`store.get_checkout_receipt_print_data` は現在のマスタをjoinせず会計のスナップショットを返す。既存行の値補完や旧JSON契約の互換は作らない。

領収書用 `print_data` は、法人名、登録番号、店舗表示名、住所、電話番号が空またはnullなら表示値として `未設定` を返す。`issuer_snapshot` 自体は補完・更新しない。ロゴは必須項目でないため、空欄または初期サンプルロゴのままとする。

サービス料は有効な `store_order_lines` 全体の小計に20%を掛け、`round(order_subtotal_amount * 0.20)` とする。標準商品、カラオケ、指名料金を含み、自由入力明細の加算・値引きはサービス料の対象に含めない。`base_amount = order_subtotal_amount + service_charge_amount`、`applied_adjustment_amount = max(adjustment_amount, -base_amount)`、`total_amount = base_amount + applied_adjustment_amount` として計算する。計算上の合計が負でも会計伝票出力・会計確定を拒否せず、0円会計として会計伝票と領収書を出力する。0円会計は決済行を作らず、超過した値引きを繰り越し・印字しない。

`checkout-statement-v1` の金額サマリーは小計、サービス料、内消費税（10%）、合計の順にする。内消費税は `consumption_tax_amount = round(total_amount * 10 / 110)` としてサーバー側で返す参考表示であり、合計へ加算しない。ブラウザ側で税額を再計算しない。

固定決済方法の現金、CAT、PAYPAYは専用seed SQLで `payment_method_master` に事前作成する。後続の `store.confirm_checkout` は固定3方法の有効行を検証・参照するだけで、マスタをinsert/updateしない。現行の会計ごとに固定3行をupsertする暫定処理は削除する。同じ決済方法コードが複数回含まれる入力はRPC側で拒否し、1会計で1方法1行を保証する。

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

通常の `store.close_business_day` は `store_slips.status in ('open', 'checkout_ready')` を未会計伝票として数え、1件でもあれば締めを拒否する。現行の `open` だけを数える判定は `checkout_ready` 導入と同じ変更単位で置き換える。`p_ignore_closing_requirements = true` の管理者モードは既存どおりこの条件を迂回できる。

`store.release_checkout_ready` の成功応答時は、同一端末の対象伝票の会計伝票印刷記録をlocalStorageから削除する。解除後は伝票内容が変わり得るため、解除前の会計伝票を印刷させない。

## 6. 呼び出し経路

Razor PageのPageModelはRepositoryを呼び、Repositoryが `ISupabaseRpcClient` を通じてRPC名とpayloadを `prosper-rpc` Edge Functionへ渡す。Edge Function側で許可済みRPCだけを実行し、Repositoryは戻り値のJSON配列またはスカラーをDTOへ変換する。

`prosper-rpc` は `json` / `jsonb` 引数を、JSの配列/オブジェクトのまま `postgres.js` へ渡す。Edge Function側で事前に `JSON.stringify` した文字列を渡すと、`postgres.js` 側のJSONシリアライズで二重エンコードされ、PostgresではJSON配列ではなくJSON文字列として扱われる。`jsonb_array_elements` を使う指名追加、注文追加、勤怠保存などのRPCでは0件登録や検証漏れにつながるため、文字列payloadはJSONとしてparseできる場合だけJS値へ戻してからSQLへ渡す。

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

`store.get_order_attending_casts` は店舗別・営業日別にアプリ側でキャッシュする。勤怠保存、退勤情報保存、営業日開始、営業日締め、キャストのドリンクメモ保存の成功時に対象営業日のキャッシュを破棄する。

`store.get_business_day_slips` と `store.get_order_entry_slips` はキャッシュ対象にしない。アプリ側ではRazor初期表示をブロックせず、ページ用JSON handlerから初回Ajax、フォーカス復帰、10秒ごとの表示中自動更新で取得する。保存成功POST直後の同期再取得は行わない。

注文端末キューと伝票詳細のオーダー追加モーダル内キューは、DB保存前の端末内または画面内状態である。RPC概要では保存後のDB状態だけを共有状態として扱い、会計前の未保存ブロック対象には、会計端末がDBまたは自画面状態から直接確認できるものだけを含める。

## 8. RPC結果ライフサイクル

Repositoryが受け取ったRPC結果は、以下のライフサイクルで扱う。キャッシュはアプリサーバープロセス内の `IMemoryCache` であり、プロセス再起動や別インスタンスには共有されない。RPC失敗、設定未完了、検証に使えない空結果はキャッシュしない。

| RPC | 結果の保持単位 | ライフサイクル |
| --- | --- | --- |
| `store.get_departments` | 店舗一覧マスタ。アプリプロセス単位。 | 初回成功時に保持する。アプリ内に店舗マスタ更新画面がないため明示破棄はせず、プロセス再起動または将来の店舗更新機能で更新する。 |
| `store.delete_non_master_records` | デバッグ削除結果のみ。 | 戻り値のテーブル別削除件数を画面結果表示に使い、キャッシュしない。成功時に現在営業日と指名バック設定のruntimeキャッシュを破棄する。 |
| `store.get_context` | 店舗別マスタ。`department_id` 単位。 | 通常画面では初回成功時に保持する。店舗別運用設定がアプリ内で更新された場合は破棄が必要。領収書保存時の会社ID取得だけは現状キャッシュを経由せず都度取得する。 |
| `store.get_tables` | 店舗別マスタ。`department_id` 単位。 | 初回成功時に保持する。卓マスタ更新をアプリ内で扱うまでは明示破棄しないため、SQL更新後はアプリ再起動または再配備で更新する。 |
| `store.get_casts` | 店舗別キャスト候補。`department_id` 単位。 | 初回成功時に保持する。`store.create_cast` / `store.delete_cast` 成功時に破棄する。 |
| `store.get_casts_admin` | 店舗別キャスト管理一覧。`department_id` 単位。 | 初回成功時に保持する。`store.create_cast` / `store.update_cast_drink_memo` / `store.delete_cast` 成功時に `store.get_casts` と同時に破棄する。 |
| `store.create_cast` | 保存結果のみ。 | 戻り値の `cast_id` を画面結果判定に使い、キャッシュしない。成功時にキャスト候補/管理一覧キャッシュを破棄する。 |
| `store.update_cast_drink_memo` | 保存結果のみ。 | 戻り値の `cast_id` を画面結果判定に使い、キャスト候補/管理一覧と、対象営業日の出勤キャスト候補キャッシュを破棄する。 |
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
| `store.get_order_attending_casts` | 店舗別・営業日別の出勤キャスト候補。`department_id` + `business_day_id` 単位。 | 初回成功時に保持する。勤怠保存、退勤情報保存、営業日開始、営業日締め、キャストのドリンクメモ保存の成功時に対象営業日のキャッシュを破棄する。退勤済みかどうかだけでは破棄しない。 |
| `store.save_business_day_attendance` | 保存結果のみ。 | 戻り値の営業日を画面結果判定に使い、キャッシュしない。成功時に対象営業日の出勤キャスト候補キャッシュを破棄する。 |
| `store.save_business_day_closing_attendance` | 保存結果のみ。 | 戻り値の保存件数を画面結果判定に使い、キャッシュしない。成功時に対象営業日の出勤キャスト候補キャッシュを破棄する。 |
| `store.get_open_slip_count` | 締め可否用の動的件数。 | キャッシュしない。`/Closing` のパネル状態JSON handler、フォーカス復帰、30秒ごとの表示中自動更新、締めPOST検証で最新取得する。 |
| `store.get_business_day_drink_delivery_status` | 締め可否用の動的状態。 | キャッシュしない。酒代入力、`/Closing` のパネル状態JSON handler、フォーカス復帰、30秒ごとの表示中自動更新、締めPOST検証で最新取得する。 |
| `store.save_business_day_drink_delivery_amount` | 保存結果のみ。 | 戻り値の保存金額を画面結果判定に使い、キャッシュしない。締め可否に関わるため次回表示で最新取得する。 |
| `store.get_business_day_closing_attendance` | 締め勤怠の動的一覧。 | キャッシュしない。出退勤保存で変わるため勤怠画面、`/Closing` のパネル状態JSON handler、締めPOST検証で最新取得する。 |
| `store.get_business_day_cast_sales_adjustment_status` | 締め可否用の動的状態。 | キャッシュしない。会計や調整保存で変わるため `/Closing` のパネル状態JSON handler、フォーカス復帰、30秒ごとの表示中自動更新、締めPOST検証で最新取得する。 |
| `store.get_cast_sales_adjustment_slips` | キャスト売上額調整対象の動的一覧。 | キャッシュしない。会計や調整保存で変わるため専用画面表示時に最新取得する。 |
| `store.get_cast_sales_adjustment_detail` | 伝票単位の調整詳細と、販売イベント時点の指名キャストで算出する小計・合計別の初期配分案。 | キャッシュしない。対象伝票の調整モーダル表示や保存検証の元データとして最新取得する。開始時刻や会計スナップショットに不足・不整合がある場合は、理由コードとともに均等配分へフォールバックする。 |
| `store.save_cast_sales_adjustment` | 保存結果のみ。 | 戻り値の保存件数を画面結果判定に使い、キャッシュしない。調整状態や対象一覧は次回表示で最新取得する。 |
| `store.get_business_day_slips` | 営業中一覧の動的結果。 | キャッシュしない。営業中トップのJSON handlerから初回表示後、フォーカス復帰、10秒ごとの表示中自動更新で取得する。後続では営業中トップに見込み売上を参考表示する。見込み売上は取消以外の伝票の会計額合計のみとし、会計額はその時点で計算できる税込・サービス料込みの最終請求額とする。更新は営業中一覧ハブと同じ10秒ポーリング/フォーカス復帰で行う。集計を同RPCに含めるか、専用軽量RPCに分けるかは実装時に決める。 |
| `store.get_order_entry_slips` | 注文端末の対象伝票動的結果。 | キャッシュしない。`/Orders` のJSON handlerから初回表示後、フォーカス復帰、10秒ごとの表示中自動更新で取得する。 |
| `store.get_slip_detail` | 伝票詳細の動的結果。 | キャッシュしない。客、指名、注文、調整、カラオケ、会計状態を含むため詳細画面や保存後再表示で最新取得する。 |
| `store.create_slip` | 保存結果のみ。 | 戻り値の `slip_id` を画面結果判定に使い、キャッシュしない。営業日が未作成の場合は事前の `EnsureCurrentAsync` が現在営業日キャッシュを作る。営業中一覧は次回JSON取得で反映する。 |
| `store.add_slip_customers` | 保存結果のみ。 | 戻り値の `inserted_count` を画面結果判定に使い、キャッシュしない。伝票詳細は次回表示で最新取得する。 |
| `store.leave_slip_customer` | 保存結果のみ。 | 戻り値は成功判定に使い、キャッシュしない。伝票詳細と営業中一覧は次回取得で反映する。 |
| `store.update_slip_customer_label` | 保存結果のみ。 | 戻り値は成功判定に使い、キャッシュしない。伝票詳細と営業中一覧は次回取得で反映する。 |
| `store.add_slip_nominations` | 保存結果のみ。 | 戻り値の `inserted_count` を画面結果判定に使い、キャッシュしない。指名種別はキャッシュ済みマスタを検証に使い、実績はRPC側で現在DBマスタからスナップショット保存する。 |
| `store.add_order_lines` | 保存結果のみ。 | 戻り値の `inserted_count` を画面結果判定に使い、キャッシュしない。注文対象伝票や伝票詳細は次回取得で反映する。 |
| `store.void_order_line` | 保存結果のみ。 | 戻り値は成功判定に使い、キャッシュしない。伝票詳細は次回取得で反映する。 |
| `store.add_slip_adjustment` | 保存結果のみ。 | 戻り値の `inserted_count` を画面結果判定に使い、キャッシュしない。伝票詳細は次回取得で反映する。 |
| `store.save_karaoke_lines` | 保存結果のみ。 | 戻り値の `saved_count` をAjax結果判定に使い、キャッシュしない。営業中一覧や伝票詳細は次回取得で反映する。 |
| `store.save_order_line_quantities` | 保存結果のみ。 | 戻り値の `saved_count` を画面結果判定に使い、キャッシュしない。通常注文数量、バック実績、伝票詳細、営業中一覧は次回取得で反映する。 |
| `store.confirm_checkout` | 保存結果のみ。 | 戻り値の `checkout_id` と `change_amount` を会計結果判定に使い、キャッシュしない。会計後の営業中一覧、注文対象伝票、締め可否は次回取得で反映する。 |
| `store.cancel_checkout` | 保存結果のみ。 | 戻り値の `checkout_id` を画面結果判定に使い、キャッシュしない。会計取消後の営業中一覧、注文対象伝票、締め可否、キャスト売上額調整状態は次回取得で反映する。客行の退店時刻は変更せず、キャスト売上額調整はリセットする。取消成功時は、この `checkout_id` の同一端末にある領収書再印刷待ちをlocalStorageから削除する。 |
| `store.get_pending_receipts` | 未処理領収書の動的一覧。 | キャッシュしない。領収書入力や締め可否に直結するため、領収書画面、Driveプレビュー許可判定、`/Closing` のパネル状態JSON handler、締めPOST検証で最新取得する。 |
| `store.quick_enter_receipt` | 保存結果のみ。 | 戻り値を保存成功判定に使い、キャッシュしない。未処理領収書一覧と締め可否は次回取得で反映する。 |
| `store.mark_receipt_scan_mistake` | 保存結果のみ。 | 戻り値を保存成功判定に使い、キャッシュしない。未処理領収書一覧と締め可否は次回取得で反映する。 |

## 9. 実装済み変更と残件

この表の「会計伝票出力と会計準備中」「0円会計」「レシートプリンター正式仕様」は、会計フロー第1段階として**ソース実装済み（未適用）**である。表中に残る「後続仕様」は、この文書を先に作成した時点のラベルであり、リモートDBとEdge Functionの適用・実機確認だけが残件である。

| 項目 | 状態 | SQL/RPC上の整理 |
| --- | --- | --- |
| カラオケ商品化 | 実装済み | `store_item_master.item_type = 'karaoke'` のシステム商品を使い、`store_order_lines` に1伝票1行で集約する。旧 `store_slip_charge_lines.charge_type = 'karaoke'` のアクティブ行は注文行へ移行してvoid化する。 |
| システム商品の注文端末除外 | 実装済み | `store.get_order_items` は標準商品だけを返し、`store.add_order_lines` も標準商品以外を拒否する。カラオケなどのシステム商品は専用RPCで保存する。 |
| カラオケ/指名料金のサービス料対象化 | 実装済み | `store.get_business_day_slips` と `store.confirm_checkout` は、カラオケや指名料金を含む全注文行の小計に20%サービス料を掛ける。システム商品も自由入力調整ではなく注文小計に含める。 |
| 自由入力調整 | 実装済み | `store_slip_charge_lines` は現行運用では `charge_type = 'adjustment'` を扱う。伝票詳細では `store.add_slip_adjustment` で1件ずつ追加し、会計額へ直接加減する。商品マスタには登録しない。後続の会計伝票では通常オーダーとは別枠で、摘要と符号付き金額だけを個別行として返す。 |
| 指名料金のシステム商品化 | 実装済み | 指名登録時に `store_item_master.item_type = 'nomination_fee'` のシステム商品を使って `store_order_lines` へ1指名1行を作成する。`store_slip_casts.nomination_price` は入力値の保持と表示に使い、会計集計は指名料金の注文行を商品小計として参照する。 |
| 指名種別別キャストバック | 実装済み | `store_nomination_back_master` で店舗別の指名種別候補と単価を管理し、`store.create_slip` / `store.add_slip_nominations` が `nomination_kind` から基本種別と同伴時刻を解決して `store_slip_cast_backs` へ営業実績を作成する。 |
| 店舗別運用設定 | 実装済み | `department_master.attendance_minute_step`, `cast_sales_amount_basis`, `cast_sales_split_mode` を `store.get_context` で返し、勤怠時刻選択とキャスト売上額調整の初期配分に使う。 |
| 管理者モード締め | 実装済み | `store.close_business_day` は `p_ignore_closing_requirements = true` の場合、未会計伝票、酒代、勤怠、退勤、キャスト売上額調整、未入力領収書の条件検証を無視して営業日を締める。営業日IDと店舗IDの一致確認は維持する。 |
| 当日出勤キャスト候補キャッシュ | 実装済み | RPC定義変更は不要。アプリ側に店舗別・営業日別キャッシュキーを持ち、勤怠保存/退勤情報保存/営業日開始/営業日締め時に破棄する。 |
| 営業中一覧と注文対象伝票の再取得削減 | 実装済み | RPC定義変更は不要。`store.get_business_day_slips` と `store.get_order_entry_slips` はページ用JSON handlerから非同期取得し、初期表示や保存成功POSTをブロックしない。 |
| 会計伝票出力と会計準備中 | 実装済み | `store.issue_checkout_statement`、`store.get_checkout_statement_print_data`、`store.release_checkout_ready` を使い、`store_slips.status` を `checkout_ready` にする。`store.issue_checkout_statement` は退店時刻固定、`checkout_ready` 化、未退店客行補完、印刷用データ返却までを1RPCで完結させ、画面は成功後に直ちに物理印刷を開始する。返却された印刷用データは物理印刷前に営業中端末の会計伝票用localStorageへ保存し、同一端末の再印刷では追加RPCを呼ばず再利用する。印刷待ち、印刷中、印刷失敗、印刷済みはDB状態にせず、この端末内記録で表す。端末内印刷状態は会計確定の条件にせず、会計準備中のモーダルで再印刷、会計準備解除、決済への移行を選べる。端末記録は会計確定の成功応答時に削除し、記録がない `checkout_ready` は再印刷用に復旧用RPCで会計伝票を再取得する。`store.confirm_checkout` は `checkout_ready` 伝票だけを対象にし、`open` からの直接確定を禁止する。会計確定自体は `checkout_ready` から `checked_out` へ遷移するDB操作である。`store.confirm_checkout` は会計伝票出力時に固定済みの `store_slips.closed_at` と支払入力を使う。`store_slip_customers.left_at_source` は、個別退店登録を `manual`、会計伝票出力補完を `accounting_slip` として記録する。出力前には対象伝票の最新保存状態を1RPCで取得し、営業中一覧の表示状態だけを根拠にしない。物理印刷失敗ではDB状態を戻さず、再印刷または解除で戻す。`store.release_checkout_ready` は `checkout_ready` だけを対象にし、`checked_out` は既存の会計取消へ分ける。再印刷は専用スナップショットを作らず、会計準備中でロックされた現在DB状態から復旧時だけ再生成する。解除RPCでは `store_slips.closed_at = null` に戻し、`accounting_slip` 由来の退店時刻だけを未確定に戻し、個別退店登録済みの退店時刻は残す。`store.add_order_lines` など会計内容を変えるRPCは `checkout_ready` を対象外にする。営業中一覧の定期更新では印刷用JSONを返さない。 |
| 0円会計 | 後続仕様 | `store.confirm_checkout` は会計額0円の場合、支払方法明細なしで会計確定できるようにする。`store_checkout_payments` に0円の現金/CAT/その他行を作らず、表示上は `請求なし 0円` として扱う。 |
| レシートプリンター正式仕様 | 後続仕様 | SII Web SDK Server経由で80mm紙向けに店舗名、宛名、会計確定の実日付、「ご飲食代として」、会計額、支払い方法、内消費税額を印字し、宛名は会計処理で入力した値に `様` を付けて表示する。未入力の場合も `様` のみ表示する。支払方法の `CAT` は「クレジット」として表示する。`checkout_id`、`slip_id`、`slip_no` は内部管理用であり紙面に印字しない。営業日は内部管理用に留める。収入印紙欄は税込み55,000円以上とし、日本の収入印紙を貼る前提のサイズに合わせて枠、余白、担当者印欄との位置関係を調整する。最初の印刷失敗通知と即時再試行は同じブラウザのlocalStorageで扱うが、DBには印刷状態、印刷ジョブ、印刷履歴を保存しない。`checked_out` の伝票はいつでも手動再発行でき、紙面には `再試行` を印字する。会計取消済み伝票は再発行できない。 |
