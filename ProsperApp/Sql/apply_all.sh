#!/usr/bin/env bash
#
# ProsperAppのDBスキーマを、記載順どおりに適用します。
#
# テスト用DBとCIが同じ順番を再現できるよう、このファイルを適用順の正本にします。
#
# 使い方:
#   Sql/apply_all.sh "postgres://user:pass@host:5432/postgres"
#   Sql/apply_all.sh --with-seeds "postgres://..."           # 店舗固有のseedも投入する
#   Sql/apply_all.sh --with-test-fixtures "postgres://..."   # テスト用のダミーカタログも投入する
#
# 全ファイルが冪等（create ... if not exists / create or replace / do $$ ... $$）なので、
# 既存DBへ再適用しても差分だけが当たります。

set -euo pipefail

WITH_SEEDS=0
WITH_TEST_FIXTURES=0
while true; do
    case "${1:-}" in
        --with-seeds)         WITH_SEEDS=1; shift ;;
        --with-test-fixtures) WITH_TEST_FIXTURES=1; shift ;;
        *)                    break ;;
    esac
done

DSN="${1:-${DATABASE_URL:-}}"
if [ -z "$DSN" ]; then
    echo "usage: $0 [--with-seeds] [--with-test-fixtures] <postgres-connection-string>" >&2
    exit 2
fi

SQL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# baseline: ProsperApp導入以前から存在していたテーブル。空のDBを立ち上げる時だけ意味を持ち、
# 本番では全て no-op になります。
BASELINE=(
    baseline/00_supabase_roles.sql
    baseline/00_company_department_master.sql
    baseline/01_accounting_tables.sql
    baseline/02_accounting_save_journal_payload.sql
    baseline/03_bootstrap_minimum.sql
    baseline/04_accounting_account_master.sql
)

# ProsperAppのテーブルと設定関数。
CORE=(
    store_order_accounting_tables.sql
    store_settings_functions.sql
)

# store schemaのRPC。依存関係に従って記載順に適用します。
RPC=(
    store_rpc/00_schema.sql
    store_rpc/00_legacy_rpc_cutover.sql
    store_rpc/00a_drink_back_schema.sql
    store_rpc/00b_app_access.sql
    store_rpc/01_business_day.sql
    store_rpc/02_store_masters.sql
    store_rpc/03_slips.sql
    store_rpc/04_orders.sql
    store_rpc/05_checkout.sql
    store_rpc/06_receipts.sql
    store_rpc/07_cast_sales_adjustments.sql
    store_rpc/11_pricing.sql
    store_rpc/12_pricing_system_items.sql
    store_rpc/08_checkout_ready.sql
    store_rpc/09_business_home_snapshot.sql
    store_rpc/12_daily_report.sql
    store_rpc/13_accounting_snapshot_guards.sql
    store_rpc/30_current_drink_back_adjustments.sql
    store_rpc/14_operational_read_models.sql
    store_rpc/17_current_business_home_snapshot.sql
    store_rpc/15_business_home_bootstrap.sql
    store_rpc/16_management_master_snapshot.sql
    store_rpc/18_current_order_entry_candidates.sql
    store_rpc/19_current_business_home_flush.sql
    store_rpc/20_current_business_day_attendance.sql
    store_rpc/21_receipt_work_queue.sql
    store_rpc/22_current_business_day_close.sql
    store_rpc/23_current_business_day_drink_delivery.sql
    store_rpc/25_current_order_submit.sql
    store_rpc/26_current_checkout_mutations.sql
    store_rpc/27_current_attendance_editor.sql
    store_rpc/28_current_closing_dashboard.sql
    store_rpc/29_current_cast_sales_adjustment.sql
    store_rpc/31_sales_history.sql
    store_rpc/32_operation_result_cleanup.sql
    store_rpc/99_grants.sql
)

# 店舗固有の初期データ。空のDBにも当てられるものだけをここに置きます。
#
# mieu_honten_product_master_seed.sql は意図的に外しています。既存カテゴリと
# 既存商品「1000」の値まで検証する一度きりの本番移行スクリプトなので、
# 空のDBでは required_category_not_found で必ず落ちます。テスト用DBの
# カタログは fixtures/test_catalog.sql が用意します。
SEEDS=(
    store_system_item_category_migration.sql
    store_table_master_seed.sql
    quick_entry_account_master_updates.sql
    receipt_issuer_master_values.sql
    receipt_honten_logo_url.sql
)

# テスト用のダミーカタログ。本番へは流さないこと。
TEST_FIXTURES=(
    fixtures/test_catalog.sql
)

FILES=("${BASELINE[@]}" "${CORE[@]}" "${RPC[@]}")
if [ "$WITH_SEEDS" = "1" ] || [ "$WITH_TEST_FIXTURES" = "1" ]; then
    FILES+=("${SEEDS[@]}")
fi
if [ "$WITH_TEST_FIXTURES" = "1" ]; then
    FILES+=("${TEST_FIXTURES[@]}")
fi

for file in "${FILES[@]}"; do
    echo "==> $file"
    psql "$DSN" \
        --set ON_ERROR_STOP=1 \
        --no-psqlrc \
        --quiet \
        --file "$SQL_DIR/$file"
done

echo
echo "applied ${#FILES[@]} files"
