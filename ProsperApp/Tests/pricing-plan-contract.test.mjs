import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const pricingSource = await readFile(new URL('../Sql/store_rpc/11_pricing.sql', import.meta.url), 'utf8');
const snapshotSource = await readFile(new URL('../Sql/store_rpc/09_business_home_snapshot.sql', import.meta.url), 'utf8');

assert.match(pricingSource, /pricing_mode = 'set_extension_v1'/, '標準料金実装を明示すること');
assert.match(pricingSource, /from generate_series\(1, greatest\(v_extension_count, 0\)\)/, 'セット終了ちょうどから延長イベントを生成すること');
assert.match(pricingSource, /c\.entered_at <= e\.occurred_at[\s\S]*c\.left_at > e\.occurred_at/, '入店を含み退店を含まない人数で計算すること');
assert.match(pricingSource, /store_slip_pricing_lines/, '会計時に固定する自動料金明細テーブルを持つこと');
assert.match(pricingSource, /slip_cast_id bigint references public\.store_slip_casts/, '将来のキャスト帰属余地を保持すること');
assert.match(snapshotSource, /store\.calculate_slip_pricing\(s\.department_id, s\.slip_id, now\(\)\)/, '営業中はサーバーで現在時刻の見積りを作ること');
assert.match(snapshotSource, /'pricingLines'/, '全伝票スナップショットに自動料金明細を含めること');

console.log('Pricing plan contract checks passed.');
