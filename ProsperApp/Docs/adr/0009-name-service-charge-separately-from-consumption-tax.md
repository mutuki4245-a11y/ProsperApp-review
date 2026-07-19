# サービス料と消費税を同じ名前で扱わない

> 実装状況（2026-07-20）: `service_charge_amount` / `ServiceChargeAmount` への切替はSQL・C#ソースに実装済み。リモートDB適用は未実施。

商品小計に対する20%のサービス料は、消費税とは別の店舗料金である。DB列、RPC JSON、C#モデルでは `service_charge_amount` / `ServiceChargeAmount` と命名する。`service_tax_amount` / `ServiceTaxAmount` は会計フロー第1段階のソースで置き換え済みであり、テスト段階のため旧契約との互換は実装しない。

## Considered Options

### 既存の名前を残し、表示文言だけをサービス料にする

DB、RPC、C#で消費税と誤読され続け、適格簡易請求書の税額表示を追加する際に意味が衝突する。

### サービス料と消費税を同じ金額として扱う

20%の店舗料金と10%固定の消費税を混同し、会計額と領収書の税額を正しく説明できない。

### 決定案

サービス料は `service_charge_amount`、消費税は税率・課税対象額から扱う別概念として分離する。会計伝票では、消費税を `内消費税（10%） = round(total_amount * 10 / 110)` として参考表示し、合計へ加算しない。
