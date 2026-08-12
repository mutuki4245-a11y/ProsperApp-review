# ProsperApp Handoff

## Current State

2026-08-12時点の正本です。RPC同期は `docs/rpc-synchronization-improvement-plan.md` の横断契約と画面別契約へ全面移行しました。旧RPC、旧Razor POST、旧DTO、旧Repository、旧payload変換、画面内fallbackは互換目的で残していません。

本番Supabaseプロジェクト `zwdecfoecgpzpkallukh` へ営業履歴・過去営業日補正SQLを適用し、`prosper-rpc` Edge Function v40とAzureアプリをdeploy済みです。旧RPCとの互換期間は設けず、Azureアプリ、DB、Edge Functionを同じ契約へ切り替えました。

確認済み:

- `dotnet build ProsperApp.csproj --no-restore`: 警告0、エラー0
- `dotnet test ../ProsperApp.Tests/ProsperApp.Tests.csproj --no-restore`: 34/34成功
- `node --test Tests/*.test.mjs`: 26/26成功
- `node --check wwwroot/js/features/*.js`: 成功
- Edge Function bundle検査: 成功
- アプリ契約34 RPC: 各1定義、旧RPC: 0定義
- `anon`、`authenticated`、`service_role` の `store` 関数直接実行権限: 0件
- ドリンクバック移行: 旧12件から新12件、合計金額一致、旧テーブル削除済み
- `prosper-rpc` v40: ACTIVE
- 営業履歴補正RPC: 実データを用いたrollback試験で変更なし、新版追加、旧版不変、監査、競合、4件目拒否、別営業日拒否、冪等再送、不完全状態維持を確認
- 公開営業履歴: 一覧、営業中誘導、締め済み日報、4タブ補正editorをdesktop/mobileで確認し、console error 0件

ローカル開発サーバーは起動していません。

## Synchronization Contract

- Razor GETは認可と状態非依存shellだけを返します。runtime snapshotを待たず、browserの `ProsperSync` / `SyncStore` がJSON readでhydrateします。
- 成功mutationは画面更新に必要なsnapshot、editor、dashboard delta、またはmaster deltaを返します。成功直後に同じ画面の全体readを実行しません。
- runtime stateをApp Serviceのmaster cacheへ混ぜません。営業日、伝票、勤怠、締め、領収書キューはcurrent read/mutation応答だけを正とします。
- revisionの古い非同期応答はbrowserで破棄します。snapshot同期はsingle-flightです。
- 全mutationは `operation_id` を受けます。結果不明時は同じpayloadとIDを再送し、確認済みの業務エラーだけを編集可能状態へ戻します。
- operation結果は原則DBで30日保持します。営業履歴補正のoperation結果は監査・長期冪等性のため削除しません。部署、営業日、対象ID、revision、管理者権限はRPC側で再検証します。
- アプリからSupabase table RESTやRPC REST fallbackを使いません。C#は `prosper-rpc` Edge Functionのallowlistだけを呼びます。

## Current RPCs

主な画面契約:

- 営業中: `get_business_home_bootstrap_v2`, `get_current_business_home_snapshot`, `sync_business_home_changes_v2`
- 注文: `get_current_order_entry_candidates`, `submit_current_order_entry_v2`
- 会計: `issue_checkout_statement_v2`, `release_checkout_ready_v2`, `confirm_checkout_v2`, `cancel_checkout_v2`
- 勤怠: `get_attendance_editor_bootstrap_v2`, `get_current_attendance_editor_snapshot`, `save_current_business_day_attendance_v2`
- 締め: `get_current_closing_dashboard`, `close_business_day_v2`
- 納品額: `get_current_drink_delivery_editor`, `save_current_business_day_drink_delivery_amount_v2`
- キャスト売上額: `get_current_cast_sales_adjustment_overview`, `save_current_cast_sales_adjustment_v2`, `confirm_current_cast_sales_adjustments_v2`
- ドリンクバック: `get_current_drink_back_editor`, `save_drink_back_adjustments_v2`
- 領収書: `get_current_receipt_work_queue`, `advance_receipt_work_queue_v2`
- 管理master: `get_management_master_snapshot`, `save_management_master_v2`
- 日報・再印刷: `get_business_day_daily_report`, `get_checkout_statement_print_data`, `get_checkout_receipt_print_data`
- 営業履歴・補正: `get_sales_history_page`, `get_sales_history_correction_editor`, `save_sales_history_correction_v1`

C#呼出し、Edge allowlist、SQL定義は `Tests/rpc-contract.test.mjs` で完全一致を検証します。旧RPC名は `Sql/store_rpc/00_legacy_rpc_cutover.sql` で削除し、SQL定義として再作成しません。v2から使う低水準関数は `_internal` 名で、全 `store` 関数の直接実行権限は `99_grants.sql` で剥奪します。

## Browser Recovery

- 営業中編集と伝票作成は店舗単位のlocalStorageへ保存します。
- 注文キューは店舗・営業日単位のlocalStorageへ保存します。結果不明の送信中はキューを編集不可にし、同じcommandを再送します。
- 会計、管理master、締め、納品額、キャスト売上額、ドリンクバックの結果不明commandはsessionStorageへ保存します。
- 領収書はIndexedDB outboxを使い、常に先頭1件だけを直列送信します。不明な通信失敗は同じ `operation_id` のまま待機し、既知の業務エラーは再編集一覧へ移します。
- 会計伝票と会計確定時の領収書印字データは端末cacheを優先します。cacheがない再印刷だけ読み取り専用RPCを使います。

## SQL Apply Order

`Sql/store_rpc_functions.sql` は説明用で実行しません。DB再構築時は次を記載順に適用します。

```text
00_schema.sql
00_legacy_rpc_cutover.sql
00a_drink_back_schema.sql
01_business_day.sql
02_store_masters.sql
03_slips.sql
04_orders.sql
05_checkout.sql
06_receipts.sql
07_cast_sales_adjustments.sql
11_pricing.sql
12_pricing_system_items.sql
08_checkout_ready.sql
09_business_home_snapshot.sql
12_daily_report.sql
13_accounting_snapshot_guards.sql
30_current_drink_back_adjustments.sql
14_operational_read_models.sql
17_current_business_home_snapshot.sql
15_business_home_bootstrap.sql
16_management_master_snapshot.sql
18_current_order_entry_candidates.sql
19_current_business_home_flush.sql
20_current_business_day_attendance.sql
21_receipt_work_queue.sql
23_current_business_day_drink_delivery.sql
25_current_order_submit.sql
26_current_checkout_mutations.sql
27_current_attendance_editor.sql
28_current_closing_dashboard.sql
29_current_cast_sales_adjustment.sql
22_current_business_day_close.sql
31_sales_history.sql
99_grants.sql
```

その後、`supabase/functions/prosper-rpc/index.ts` を同じリリースのEdge Functionとしてdeployします。本番では2026-08-12にv40まで適用済みです。旧アプリと新DB、または新アプリと旧DBを混在させないでください。

## Irreversible Migration

`00a_drink_back_schema.sql` は `store_business_day_champagne_backs` の既存行を `store_business_day_drink_back_adjustments` へ一方向移行し、競合時も既存日次行を保持したうえで旧テーブルを削除します。旧シャンパンバックRPCも削除します。

この変更はGit revertだけでは戻りません。旧版へ戻す必要がある場合は、アプリを戻す前に次のDB作業が必要です。

1. v2アプリの書込みを停止する。
2. 新テーブルの符号付き調整額を旧 `back_amount` へ戻せるか業務判断する。
3. 旧物理テーブルと旧RPCを明示SQLで再作成し、必要な行を逆移行する。
4. 旧 `prosper-rpc` allowlistと旧アプリを同時にdeployする。

符号付き任意調整は旧モデルで表現できないため、機械的な完全ロールバックは保証しません。通常の戻し方はv2 SQLの前進修正です。

営業履歴補正では `ux_store_business_day_closing_snapshots_day` を削除し、`(business_day_id, snapshot_version)` の一意制約で複数の不変版を保持します。Git revertでは適用済みSQLや補正済みデータは戻りません。誤補正は旧版を更新・削除せず、正しい状態への逆補正を新しい版として追加します。Webを戻す場合も、補正済みスナップショットを保持したまま前進修正するのが基本です。

## Post-Apply Checks

1. Edge allowlistとC# RPC名が `rpc-contract.test.mjs` と一致すること。
2. `store` schemaの旧RPC名が0件であること。
3. `public`、`anon`、`authenticated`、`service_role` が `store` 関数を直接実行できないこと。
4. master cache cold/warmの双方で営業中トップが表示でき、warm GET中のRPCが0回であること。
5. 営業中トップ初期同期がcurrent snapshot 1回で、同時poll/focus/onlineが1本にまとまること。
6. 各mutationを同じ `operation_id` で再送して同じ確定結果が返ること。
7. 別端末revision競合、営業日切替、締め後更新拒否が各画面でconflictとして扱われること。
8. 成功mutation直後に同画面の全体readが送信されないこと。
9. 旧シャンパンバック行数・金額と新ドリンクバック移行結果を照合し、旧テーブル削除を確認すること。

## Security And Operations

- 店舗境界は `department_master.department_id` です。browserの部署IDやrevisionを認可根拠にしません。
- RLSを有効にし、アプリ操作は `security definer` と固定 `search_path` の関数に閉じます。
- `/Settings` の設定変更と管理者modeはserver sessionで保護します。CookieやlocalStorageを管理者権限の根拠にしません。
- 領収書プレビューは許可対象Driveファイルだけを扱います。
- 秘密情報、publish profile、Edge keyをログ・文書・Gitへ含めません。

公開URLは `https://prosper-web-cuawe7gfgtcaewgj.eastasia-01.azurewebsites.net/` です。未認証時はGoogle認証へリダイレクトされます。
