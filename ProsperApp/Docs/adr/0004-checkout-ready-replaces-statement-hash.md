# 会計準備中の不変性で会計伝票ハッシュを不要にする

> 実装状況（2026-07-20）: 新しい `store.confirm_checkout(p_department_id, p_slip_id, p_payments, p_received_amount)` 契約としてソース実装済み。リモートDB適用とEdge Functionデプロイは未実施。

会計確定では `statement_hash` や明細スナップショットを受け取らない。会計伝票出力で伝票を `checkout_ready` にし、全編集を禁止したうえで、会計確定は固定済み退店時刻と支払入力だけを検証する。単一会計端末運用では通常の不一致を前提にしないため、ハッシュ生成・照合・不一致復旧の複雑さを持ち込まない。

## Considered Options

- `checkout_ready` の不変性を会計伝票確認の境界にする。
- 会計伝票出力時のハッシュを会計確定時に照合する。
