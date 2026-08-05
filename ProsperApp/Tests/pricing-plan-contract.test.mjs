import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const pricingSource = await readFile(new URL('../Sql/store_rpc/11_pricing.sql', import.meta.url), 'utf8');
const snapshotSource = await readFile(new URL('../Sql/store_rpc/09_business_home_snapshot.sql', import.meta.url), 'utf8');
const systemItemSource = await readFile(new URL('../Sql/store_rpc/12_pricing_system_items.sql', import.meta.url), 'utf8');
const edgeFunctionSource = await readFile(new URL('../supabase/functions/prosper-rpc/index.ts', import.meta.url), 'utf8');

assert.match(pricingSource, /pricing_mode = 'set_extension_v1'/, '標準料金実装を明示すること');
assert.match(pricingSource, /extension_minutes integer not null default 30/, '延長時間は30分を初期値にすること');
assert.match(pricingSource, /chk_store_pricing_plan_extension_minutes/, '延長時間は5分単位で制約すること');
assert.match(pricingSource, /p_as_of < v_slip\.opened_at \+ \(v_plan\.set_minutes \* interval '1 minute'\)/, 'セット終了までは延長イベントを作らないこと');
assert.match(pricingSource, /v_plan\.set_minutes \+ \(\(n - 1\) \* v_plan\.extension_minutes\)/, '延長イベントはセット終了後に延長時間ごとに作ること');
assert.match(pricingSource, /from generate_series\(1, greatest\(v_extension_count, 0\)\)/, 'セット終了ちょうどから延長イベントを生成すること');
assert.match(pricingSource, /create or replace function store\.save_pricing_plan_v2/, '延長時間を保存するRPCを持つこと');
assert.match(pricingSource, /p_extension_minutes integer/, '延長時間を保存RPCの引数に含めること');
assert.match(edgeFunctionSource, /"store\.save_pricing_plan_v2"/, 'Edge Functionは延長時間対応の保存RPCを呼び出すこと');
assert.match(edgeFunctionSource, /p_extension_minutes", type: "integer", defaultValue: 30/, '旧アプリからの保存でも延長時間を30分で補うこと');
assert.match(pricingSource, /c\.entered_at <= e\.occurred_at[\s\S]*c\.left_at > e\.occurred_at/, '入店を含み退店を含まない人数で計算すること');
assert.match(pricingSource, /store_slip_pricing_lines/, '会計時に固定する自動料金明細テーブルを持つこと');
assert.match(pricingSource, /slip_cast_id bigint references public\.store_slip_casts/, '将来のキャスト帰属余地を保持すること');
assert.match(snapshotSource, /store\.calculate_slip_pricing\(s\.department_id, s\.slip_id, p_as_of\)/, '指定した基準時刻で見積りを作ること');
assert.match(snapshotSource, /store\.get_business_day_snapshot_at\([\s\S]*now\(\)/, '営業中の通常取得はサーバー現在時刻を使うこと');
assert.match(snapshotSource, /'pricingLines'/, '全伝票スナップショットに自動料金明細を含めること');
assert.match(snapshotSource, /'automatic_pricing'::text as source_type/, '営業中の見積りもシステム商品形式で明細へ含めること');
assert.match(snapshotSource, /'isDynamicPricing'/, '楽観更新時に見積りを二重計上しないこと');
assert.match(systemItemSource, /'set_fee'/, 'セット料金のシステム商品種別を持つこと');
assert.match(systemItemSource, /'extension_fee'/, '延長料金のシステム商品種別を持つこと');
assert.match(systemItemSource, /'automatic_pricing'/, 'システム商品行の生成元を区別すること');

console.log('Pricing plan contract checks passed.');
