import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../wwwroot/js/features/sii-receipt-printer.js', import.meta.url), 'utf8');

assert.match(source, /appendBinary/, 'SII Web SDKのappendBinaryで制御コマンドを送ること');
assert.match(source, /GS,\s*0x21/, '文字倍率はESC\/POSのGS !で指定すること');
assert.match(source, /GS,\s*0x76,\s*0x30/, '画像はESC\/POSのGS v 0ラスター画像で送ること');
assert.match(source, /createRasterBitImageCommand/, 'ロゴ画像のラスターコマンド生成を持つこと');
assert.match(source, /rasterizeLogo/, 'ロゴ画像を1bitラスターに変換すること');
assert.match(source, /buildReceiptPrintPlan/, 'レイアウト層の印刷プランに従って送信すること');
assert.match(source, /characterSizeByte\(style\.width,\s*style\.height\)/, '印刷プランの文字倍率を制御コマンドへ反映すること');

console.log('Receipt printer command checks passed.');
