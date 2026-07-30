# ProsperApp マスタ変更影響調査レポート

作成日: 2026-07-30

## 実装追補（2026-07-30）

本レポートの調査後、採用方針を実装し対象Supabaseへ適用した。以下は後続実装として、本レポート内の「現在の問題」記載より優先する。

- `department_id`、`cast_id`、`table_id` と所属会社・店舗をDBトリガーで不変にした。集計はIDを使い、名称集計は現在マスタを参照する。
- `open` 伝票は現在マスタ・現在料金を使う。営業中に変更した料金が既存在席伝票へ反映される運用は維持した。
- `open -> checkout_ready` で会計伝票、確認表示、営業中一覧の伝票payloadを `store_slip_accounting_snapshots` へ版管理して固定する。会計確定も固定payloadの合計を使い、発行後の計算ロジック変更を受けない。
- 会計準備解除と会計取消は旧snapshotを削除せず無効化し、固定料金行と対応する自動料金商品行をvoid化する。再発行はsnapshot版を増やすため、取消後の二重計上経路を解消した。
- 営業日締めは、決済、勤怠、キャスト売上額調整を含む `store_business_day_closing_snapshots` を同一トランザクションで保存してから `closed` にする。締め後は関連する伝票系13テーブルをDBトリガーで更新・削除不能にした。
- 営業日締めは営業日行を先にロックし、関連行更新側も同じ行を共有ロックする。締めと会計・取消・勤怠・売上配分保存を直列化し、`business_day_id` と `slip_id` の所属不一致も拒否する。
- 会計準備解除、会計取消、売上配分保存は伝票ロックで直列化した。売上配分の再保存・取消では旧行を削除せず `cancelled` として残し、`confirmed` 版だけを一意にした。
- 強制締め・backfillの料金計算は現在時刻ではなく締め時刻を使う。既存会計のbackfill金額は再計算せず、確定済み `store_checkouts` と照合・統一する。
- 標準商品の削除は物理削除と使用済み注文行の `item_id` NULL化をやめ、`is_active = false` の論理削除へ変更した。
- 導入前データは、会計済み伝票1件と締め済み営業日1件を導入時点の値でbackfillした。これは当時の名称を復元した値ではなく、導入後の変更に対する基準値である。

実DBでは、マスタ名変更後も会計伝票と営業中一覧の固定payloadが不変、準備解除後の自動料金0件、再発行時の版増加、会計取消後のactive snapshot・自動料金0件、固定会計額での確定、強制締め後の更新拒否、日次snapshot更新拒否を1トランザクション内で確認し、全変更をロールバックした。追加レビュー後は、backfill金額差異0件、締めsnapshot policy version 2、締め後ガード13テーブル、直接RPC権限0件を確認した。会計発行から売上配分2回保存・取消までの試験は `integration_ok`、強制締めと締め後更新拒否の試験は `close_integration_ok` で、いずれも全変更をロールバックした。最終件数もopen営業日1件、open伝票2件、伝票snapshot 1件、締めsnapshot 1件で試験前と一致する。UI/API契約は変更していないため、アプリ側の導線変更はない。

残存境界として、営業日IDを持たない領収書文書の状態変更は締め行ロックと直列化されない。導入前backfillの明細配列は導入時点の既存行から再構成した参考情報で、確定会計の集計額だけを正本とする。また、公開RPCには伝票の営業日移動経路がなく直接権限もないが、postgres管理SQLによる `store_slips.business_day_id` 変更を拒否する専用制約までは設けていない。

## 調査範囲と前提

- 店舗マスタ系の変更が、営業中伝票、会計準備中伝票、会計済み伝票、締め済み営業日、バック・勤怠・領収書・仕訳へ及ぼす影響を静的に調査した。
- 本レポートでは `open` を「営業中伝票」、`checkout_ready` を「会計準備中伝票」、`checked_out` を「会計済み伝票」と呼ぶ。
- 会計済みでも営業日が `open` の間は会計取消が可能であり、締め済み営業日の履歴とは扱いが異なる。
- リポジトリのSQLを正本として判定した。接続済みSupabaseの実スキーマとデータは変更せず、実DBとの差分確認も行っていない。
- 以前の指示に従って `CONTEXT.md` は再作成せず、業務用語は本レポート内で定義した。

## 結論

金額については、多くの業務値が発生時点でスナップショット保存されている。

- 注文の商品名、単価、金額
- 商品バックの単価、金額
- 指名種別、指名料金、指名バックの単価、金額
- 会計準備時に確定したセット・延長料金
- 会計時の小計、サービス料、合計
- 会計時の支払方法コード、名称、金額
- 会計時の領収書発行者情報
- 保存済みキャスト売上配分の基準額、配分方式、配分額

このため、通常の商品価格変更やバック単価変更は、保存済みの伝票・会計金額を直接書き換えない。

一方で、次の値はマスタを都度参照して表示するため、過去データの見え方が後から変わる。

- 卓番コード、卓番名
- キャスト名、所属店舗名
- 指名種別の表示名
- 一部の明細種別判定

最大の業務リスクは時間料金マスタである。営業中伝票は伝票開始時の料金プランを保持せず、常に現在有効な料金プランで全滞在時間を再計算する。そのため、営業中に料金を変更すると、変更前から在席している伝票のセット料金・延長料金まで遡って変わる。

また、会計取消処理は固定済み時間料金を無効化しない。取消後の営業中表示では、以前の固定料金と新しい動的見積りが同時に残る経路があり、料金プラン変更の有無にかかわらず二重計上リスクがある。

営業日締めは営業日を `closed` に更新するだけで、日次合計や名称を別スナップショットへ固定しない。締め済み営業日の金額も取引行・会計行・バック行から再構成され、名称を現在マスタからJOINする照会では過去表示が変わる。

根拠:

- `Sql/store_rpc/01_business_day.sql:690`
- `Sql/store_rpc/01_business_day.sql:742`

## 現在の変更手段

| マスタ | アプリ画面で可能な変更 | 画面外の変更 |
|---|---|---|
| 卓番 | なし | SQL・既存DB運用 |
| キャスト | 新規、ドリンクメモ、論理削除 | 表示名、加入日、並び順などはSQL |
| 商品カテゴリ | 新規、名称・コード・並び順・有効状態の更新 | SQL |
| 商品 | 新規、並び替え、物理削除 | 既存商品の名称・価格・バック設定更新はRPC上可能だが現画面には編集導線なし |
| 指名バック | バック単価、有効状態 | 表示名、種別、時刻、並び順はSQL。保存RPC自体は全項目を受け取る |
| 時間料金 | セット時間、セット・延長単価、有効状態 | SQL |
| 支払方法 | なし | SQL・初期投入 |
| 会社・店舗 | 端末で利用店舗を選ぶだけで、マスタ自体は更新しない | SQL・既存管理機能 |
| 勘定科目・補助科目 | なし | SQL・既存会計機能 |

根拠:

- `Pages/Management/Casts.cshtml.cs:46`
- `Pages/Management/Casts.cshtml.cs:76`
- `Pages/Management/Casts.cshtml.cs:106`
- `Pages/Management/Items.cshtml.cs:47`
- `Pages/Management/Items.cshtml.cs:82`
- `Pages/Management/Items.cshtml.cs:89`
- `Pages/Management/Items.cshtml.cs:114`
- `Pages/Management/Items.cshtml.cs:144`
- `Pages/Management/NominationBacks.cshtml.cs:31`
- `Pages/Management/Pricing.cshtml.cs:27`
- `Pages/Settings/Index.cshtml.cs:207`

## 影響一覧

| マスタ変更 | 営業中伝票 | 会計準備中 | 会計済み・締め済み |
|---|---|---|---|
| 卓番名・コード | 表示が即時変更。金額不変 | 会計伝票を再表示すると変更後名称 | 金額不変。履歴照会時の名称は変更後 |
| キャスト名 | 指名・バック表示が変更後名称 | 会計伝票の担当名が変更後名称 | バック・勤怠・配分の表示名が変更後 |
| キャスト削除 | 既存指名と金額は残るが、新規指名・新規バック指定不可 | 既存会計準備は残る | 履歴と金額は残る |
| カテゴリ変更 | 注文タブと並び順だけに影響 | 影響なし | 影響なし。ただし将来カテゴリ別集計を現マスタJOINで作ると再分類される |
| 商品名・価格 | 変更後の新規注文だけ新値。既存行は旧値 | 既存行は旧値 | 旧名称・旧単価・旧金額を保持 |
| 商品バック単価 | 変更後の新規注文だけ新値 | 既存バック額は旧値 | 旧バック単価・旧バック額を保持 |
| 商品削除 | 既存行は残り、商品IDだけNULL。新規注文不可 | 金額と商品名は残る | 金額と商品名は残る |
| 指名表示名 | 既存指名の表示だけ変更後名称 | 表示だけ変更 | 過去の配分画面も変更後名称 |
| 指名種別・時刻 | 新規指名だけ新値。既存指名の種別・開始時刻は旧値 | 旧値を保持 | 旧値を保持 |
| 指名料金 | 画面入力値を保存するため既存分不変 | 不変 | 不変 |
| 指名バック単価 | 新規指名だけ新値 | 既存バック額は旧値 | 旧バック単価・旧バック額を保持 |
| 時間料金 | 既存の全営業中伝票を現在プランで再計算 | 発行済み料金は固定 | 固定済み料金・会計合計は不変 |
| 支払方法名 | 会計確定前の選択可否・保存名に影響 | 確定時に現マスタで再検証 | 支払コード・名称・金額を保持 |
| 店舗・会社の領収書情報 | 影響なし | 会計確定時まで変更可能 | 発行者スナップショットを保持 |
| 配分基準・方式 | 影響なし | 影響なし | 締め前のキャスト売上配分に現設定を使用。保存済み行の再保存に注意 |
| 勤怠時刻刻み | 勤怠入力候補が変更 | 影響なし | 保存済み時刻は不変 |

## マスタ別の詳細

### 1. 卓番マスタ

伝票は `table_id` だけを保存し、卓番コードと卓番名を保存しない。営業中一覧、注文対象一覧、会計伝票、キャスト売上配分は、表示時に `store_table_master` をJOINする。

したがって、卓番名・コード変更は会計金額を変えないが、現在伝票と過去伝票の表示名を同時に変更する。監査時に「当時どの卓番名だったか」は復元できない。

`is_active=false` にすると新規伝票作成対象から外れる。既存伝票のJOINには有効条件がないため表示は残る。物理削除は `store_slips.table_id` の外部キーに削除動作が指定されておらず、参照済みなら通常は拒否される。

根拠:

- `Sql/agent_schema_reference.sql:168`
- `Sql/agent_schema_reference.sql:332`
- `Sql/store_rpc/02_store_masters.sql:19`
- `Sql/store_rpc/05_checkout.sql:162`
- `Sql/store_rpc/08_checkout_ready.sql:43`
- `Sql/store_rpc/09_business_home_snapshot.sql:27`
- `Sql/store_rpc/07_cast_sales_adjustments.sql:77`

### 2. キャストマスタ

指名、出勤、商品バック、指名バック、売上配分は `cast_id` を保存する。金額系テーブルにはバック単価・バック額・配分額が別途保存されるため、キャストマスタ変更で金額は変わらない。

キャスト名と所属店舗名のスナップショットはない。営業中一覧、会計伝票、勤怠締め、キャスト売上配分は現在の `cast_master.display_name` と `department_master.department_name` を表示する。DB上で表示名を変更すると、過去の表示名も変わる。

画面の削除は物理削除ではなく `status='inactive'`、`is_active=false` への論理削除である。既存の外部キーと履歴金額は維持される。一方、削除後は新規指名、出勤登録、新規の商品バック指定で有効キャスト条件を満たさない。

営業中に削除した場合、既存伝票の指名表示は残るが、そのキャストへ追加ドリンクバックを付けられない。さらに出勤キャスト一覧はメモリキャッシュされ、削除処理では営業日別キャッシュを消していないため、画面には残ってRPCで拒否される状態が継続する可能性がある。

ドリンクメモ、加入日、並び順は金額・会計には影響しない。勤怠や選択肢の表示と順序だけに影響する。

根拠:

- `Sql/agent_schema_reference.sql:310`
- `Sql/agent_schema_reference.sql:376`
- `Sql/agent_schema_reference.sql:420`
- `Sql/agent_schema_reference.sql:445`
- `Sql/agent_schema_reference.sql:544`
- `Sql/store_rpc/02_store_masters.sql:211`
- `Sql/store_rpc/03_slips.sql:205`
- `Sql/store_rpc/04_orders.sql:126`
- `Sql/store_rpc/09_business_home_snapshot.sql:59`
- `Sql/store_rpc/07_cast_sales_adjustments.sql:113`
- `Infrastructure/Supabase/SupabaseStoreCastAdminRepository.cs:152`
- `Infrastructure/Supabase/StoreMasterCacheKeys.cs:65`

### 3. 商品カテゴリマスタ

カテゴリは注文候補の抽出・分類・並び順に使われ、注文行には保存されない。カテゴリ名、コード、並び順の変更は既存伝票と会計額に影響しない。

カテゴリを無効化すると、`store.get_order_items` が有効カテゴリとの内部JOINを要求するため、配下の有効商品も注文候補から一括で消える。既存注文行は残る。

ただし注文保存RPCは商品自体の有効状態だけを再検証し、カテゴリの有効状態を検証しない。無効化前から開いていた画面や未送信キューに商品IDが残っている場合、その商品を新規注文として保存できる。表示上の無効化と保存時の不変条件が一致していない。

現行の会計集計はカテゴリを参照しない。ただし、将来カテゴリ別売上を `store_order_lines -> store_item_master -> store_item_category_master` の現在値で集計すると、カテゴリ変更で過去売上が再分類され、商品削除済み行は分類不能になる。この用途では注文時カテゴリのスナップショットが必要である。

根拠:

- `Sql/agent_schema_reference.sql:203`
- `Sql/agent_schema_reference.sql:396`
- `Sql/store_rpc/02_store_masters.sql:346`
- `Sql/store_rpc/02_store_masters.sql:375`
- `Sql/store_rpc/02_store_masters.sql:653`
- `Sql/store_rpc/04_orders.sql:109`

### 4. 商品マスタ

通常注文時に、商品IDに加えて商品名、単価、数量、金額を `store_order_lines` へ保存する。商品名・価格を変更しても既存行は更新されず、変更後に追加した注文だけが新しい値になる。

注文保存RPCはブラウザが保持する表示価格を信用せず、商品IDからDBの最新マスタを読み直す。価格変更前に注文端末へ入れた未送信キューでも、変更後に送信すれば新価格で保存される。端末表示と実際の登録価格が一時的に異なる可能性がある。

商品バックも、注文時にバック種別、数量、バック単価、バック額を `store_order_line_cast_backs` へ保存する。バック対象や単価を変更しても既存バック額は変わらない。

商品削除RPCは、全注文行の `item_id` をNULLにした後で商品を物理削除する。商品名、単価、金額は残るため会計合計は維持され、営業中伝票では数量変更と取消も可能である。ただし商品種別やカテゴリをマスタから追えなくなるため、将来の商品別・カテゴリ別履歴集計の精度を落とす。

セット・延長・指名料などのシステム商品は画面から削除・更新できない。セット・延長商品は会計伝票発行時に自動修復され、注文行にはその時点の料金行名・単価・金額が入る。

カラオケ商品は例外で、営業中に数量保存を行うたび、現在の商品名・価格で既存カラオケ行を上書きする。カラオケ価格をSQLで変更した場合、未会計伝票は次のカラオケ保存時に新価格へ変わる。会計済み伝票には保存処理が通らない。

根拠:

- `Sql/agent_schema_reference.sql:396`
- `Sql/agent_schema_reference.sql:420`
- `Sql/store_rpc/04_orders.sql:109`
- `Sql/store_rpc/04_orders.sql:148`
- `Sql/store_rpc/04_orders.sql:172`
- `Sql/store_rpc/02_store_masters.sql:814`
- `Sql/store_rpc/02_store_masters.sql:913`
- `Sql/store_rpc/03_slips.sql:717`
- `Sql/store_rpc/03_slips.sql:797`
- `Sql/store_rpc/03_slips.sql:518`
- `Sql/store_rpc/03_slips.sql:627`
- `Sql/store_rpc/12_pricing_system_items.sql:26`

### 5. 指名バックマスタ

指名追加時に `nomination_kind`、`nomination_type`、指名料金、開始時刻を伝票へ保存し、指名バック単価・金額も別テーブルへ保存する。指名バック単価や種別を後から変更しても、既存指名と既存バック金額は変わらない。

現在の管理画面で編集できるのはバック単価と有効状態だけで、表示名、種別、同伴時刻、並び順はhidden項目として往復する。これらの変更影響はSQLまたは別管理機能で更新した場合の判定である。

表示名だけはスナップショットされていない。営業中一覧とキャスト売上配分は、保存済み `nomination_kind` で現在のマスタをJOINするため、表示名変更は過去表示にも反映される。マスタ行がない場合は保存済み種別キーへフォールバックする。

無効化後は新規指名に使えないが、既存指名・既存バックは維持される。種別キー `nomination_kind` は参照上の実質的な識別子であり、同じキーの意味を別用途へ変更すると、保存済み指名の表示だけ新しい意味に見えるため、キーの再利用は禁止すべきである。

根拠:

- `Sql/agent_schema_reference.sql:244`
- `Sql/agent_schema_reference.sql:376`
- `Sql/agent_schema_reference.sql:445`
- `Sql/store_rpc/03_slips.sql:179`
- `Sql/store_rpc/03_slips.sql:219`
- `Sql/store_rpc/03_slips.sql:239`
- `Pages/Management/NominationBacks.cshtml:35`
- `Pages/Management/NominationBacks.cshtml:45`
- `Sql/store_rpc/09_business_home_snapshot.sql:298`
- `Sql/store_rpc/07_cast_sales_adjustments.sql:470`

### 6. 時間料金マスタ

営業中伝票は料金プランID・版を伝票開始時に固定しない。スナップショット取得のたび、現在有効な `store_pricing_plan_master` を使って、伝票開始時刻から現在までのセット・延長料金を全件再計算する。

例:

1. 20:00に旧プランで入店する。
2. 22:00にセット料金またはセット時間を変更する。
3. 次回同期後、20:00開始分を含む全料金が新プランで再計算される。

画面にも「営業中の見積りは次回同期時に更新」と表示されるため、これは現在の実装上は明示された挙動である。ただし「変更時刻以降だけ新料金」や「次の営業日から新料金」ではない。

会計伝票発行時には、料金プランID、版、料金種別、発生時刻、人数、数量、単価、金額を `store_slip_pricing_lines` に保存し、対応するシステム商品行も作る。`checkout_ready` と `checked_out` の金額は、その後のマスタ変更で変わらない。

会計準備を解除すると固定行を無効化し、次の発行時に現在プランで作り直す。そのため、解除と再発行を挟むと新料金へ変わる。

料金プランは版が上がっても同じマスタ行を更新する。料金行には版と結果金額が残るが、旧版のセット時間・全単価を保持する履歴マスタはない。金額監査はできても「当時の設定一式」を完全には復元できない。

重大な別問題として、会計確定後の `cancel_checkout` は、固定済み料金行と自動商品行を無効化しないまま伝票を `open` に戻す。営業中スナップショットは既存の自動商品行を注文小計に含め、さらに現在プランの動的料金を加えるため、二重計上経路がある。料金変更後の会計取消では旧料金と新料金が混在し得る。

根拠:

- `Sql/store_rpc/11_pricing.sql:103`
- `Sql/store_rpc/11_pricing.sql:148`
- `Sql/store_rpc/11_pricing.sql:166`
- `Sql/store_rpc/11_pricing.sql:265`
- `Sql/store_rpc/11_pricing.sql:329`
- `Sql/store_rpc/09_business_home_snapshot.sql:97`
- `Sql/store_rpc/09_business_home_snapshot.sql:121`
- `Sql/store_rpc/08_checkout_ready.sql:289`
- `Sql/store_rpc/08_checkout_ready.sql:341`
- `Sql/store_rpc/08_checkout_ready.sql:437`
- `Sql/store_rpc/05_checkout.sql:10`
- `Sql/store_rpc/05_checkout.sql:73`
- `Sql/store_rpc/05_checkout.sql:87`
- `Pages/Management/Pricing.cshtml.cs:45`

### 7. 支払方法マスタ

会計確定時に支払方法コードで有効マスタを再検索する。名称変更は確定前の伝票金額に影響しないが、確定時に保存される支払方法名へ反映される。

確定後は `store_checkout_payments` に支払方法ID、コード、名称、金額を保存し、領収書再印刷もこのスナップショットを使う。過去の支払表示と金額はマスタ名称変更で変わらない。

無効化すると新規会計で選べない。画面は `cash`、`cat`、`paypay` を固定表示しており、マスタの有効状態を事前取得しないため、無効な方法を選択できた後に会計確定RPCで拒否される。新しい支払方法をマスタに追加しても画面には出ない。

マスタの `requires_received_amount` と `sort_order` も現在の画面生成・確定処理では使われず、現金の受取額要否と表示順はJavaScript側の固定実装で決まる。

参照済み支払方法の物理削除は、会計・支払テーブルの外部キーに削除動作がないため通常は拒否される。履歴保護の面では論理無効化が適切である。

根拠:

- `Sql/agent_schema_reference.sql:271`
- `Sql/agent_schema_reference.sql:502`
- `Sql/agent_schema_reference.sql:526`
- `Sql/store_rpc/08_checkout_ready.sql:655`
- `Sql/store_rpc/08_checkout_ready.sql:730`
- `Sql/store_rpc/08_checkout_ready.sql:500`
- `wwwroot/js/features/business-checkout.js:231`

### 8. 会社・店舗マスタ

店舗名、会社名、インボイス登録番号、領収書表示名、住所、電話、ロゴは会計確定時に `issuer_snapshot` として保存される。会計済み領収書の再印刷は保存済み発行者情報を使うため、後からマスタを変更しても過去領収書は変わらない。

会計伝票は `checkout_ready` の表示時に現在の店舗名と卓番名を読む。会計伝票発行後、会計確定前に領収書情報を変えると、先に印刷した会計伝票と確定後の領収書で店舗表示が異なる可能性がある。

`attendance_minute_step` は勤怠入力候補だけを変更し、保存済み日時は変えない。ただし既存時刻が新しい刻みの候補外になると、画面再保存時に時刻の選び直しが必要になる。

`cast_sales_amount_basis` と `cast_sales_split_mode` は、会計金額ではなく締め時のキャスト売上配分初期値と保存メタデータへ影響する。保存済み配分額自体は残るが、一括確認処理は全対象伝票を現在設定で再保存する。設定変更後に一括確認すると、既存の配分額を維持したまま、基準種別・方式・基準額だけを現在設定へ差し替える可能性があり、監査メタデータが実際の算定経緯と一致しなくなる。

端末の管理者設定で行う利用店舗・画面モード・テーマ変更はローカルCookieの変更であり、会社・店舗マスタや既存伝票を更新しない。

根拠:

- `Sql/store_rpc/08_checkout_ready.sql:702`
- `Sql/store_rpc/08_checkout_ready.sql:718`
- `Sql/store_rpc/08_checkout_ready.sql:500`
- `Sql/store_order_accounting_tables.sql:14`
- `Sql/store_rpc/01_business_day.sql:8`
- `Pages/Attendance.cshtml.cs:172`
- `Pages/Attendance.cshtml.cs:416`
- `Pages/Closing/CastSalesAdjustment.cshtml.cs:124`
- `Pages/Closing/CastSalesAdjustment.cshtml.cs:157`
- `Sql/store_rpc/07_cast_sales_adjustments.sql:652`
- `Sql/store_rpc/07_cast_sales_adjustments.sql:676`

### 9. 勘定科目・補助科目マスタ

会計スキーマ参照では、仕訳明細に勘定科目名・補助科目名のスナップショット列がある。この設計どおり仕訳が作成されていれば、マスタ名称変更は過去仕訳の表示名を変えない。

ただし現在の領収書クイック入力画面は勘定科目・補助科目マスタを読まず、選択肢をPageModelに固定定義している。Repositoryは仕訳JSONを作るが、リポジトリ内の `store.quick_enter_receipt` SQLは `p_journal_payload` を受け取るだけで使用せず、`documents` の日付、金額、タイトル、メモ、状態だけを更新する。

このため、リポジトリだけからは「領収書保存で仕訳が作られる」と確認できず、勘定科目マスタ変更が現在の領収書処理へ与える直接影響も確認できない。接続先DBの別実装、Edge Functionの補完処理、トリガーの有無を実DBで確認する必要がある。

根拠:

- `Sql/agent_schema_reference.sql:126`
- `Sql/agent_schema_reference.sql:136`
- `Sql/quick_entry_account_master_updates.sql:4`
- `Sql/quick_entry_account_master_updates.sql:73`
- `Pages/Closing/Receipts.cshtml.cs:32`
- `Infrastructure/Supabase/SupabaseReceiptRepository.cs:156`
- `Infrastructure/Supabase/SupabaseReceiptRepository.cs:211`
- `Sql/store_rpc/06_receipts.sql:41`
- `Sql/store_rpc/06_receipts.sql:49`
- `Sql/store_rpc/06_receipts.sql:59`

## マスタではない固定業務値

現在のサービス料率20%と領収書の消費税率10%はマスタ値ではなくSQLに固定されている。会社・店舗・料金プランの変更では変わらず、率を変更するにはSQLの業務ロジック変更が必要である。

根拠:

- `Sql/store_rpc/08_checkout_ready.sql:80`
- `Sql/store_rpc/08_checkout_ready.sql:637`
- `Sql/store_rpc/08_checkout_ready.sql:529`

## キャッシュと反映タイミング

アプリ経由の商品、カテゴリ、キャスト、指名バック変更は対応キャッシュを削除する。一方、卓番、店舗コンテキスト、店舗一覧は削除経路がなく、全マスタキャッシュには有効期限がない。

SQLで直接マスタを変更した場合、次の不一致がアプリ再起動まで残り得る。

- 新規伝票の卓番候補が旧状態のまま
- キャスト候補、商品候補、指名候補が旧状態のまま
- 勤怠刻み、キャスト売上配分基準が旧状態のまま
- 店舗名・店舗有効状態が旧状態のまま

さらにブラウザへ渡した商品・支払方法候補はページを開いた時点の値であり、別端末でマスタ変更しても既存タブには自動配信されない。古い候補で送信し、RPCで拒否される可能性がある。

根拠:

- `Infrastructure/Supabase/StoreMasterCacheKeys.cs:27`
- `Infrastructure/Supabase/StoreMasterCacheKeys.cs:43`
- `Infrastructure/Supabase/StoreMasterCacheKeys.cs:49`
- `Infrastructure/Supabase/StoreMasterCacheKeys.cs:55`
- `Infrastructure/Supabase/SupabaseStoreSlipRepository.cs:29`
- `Infrastructure/Supabase/SupabaseStoreSlipRepository.cs:67`
- `Infrastructure/Supabase/SupabaseStoreOrderRepository.cs:53`
- `Infrastructure/Supabase/SupabaseNominationBackAdminRepository.cs:24`

## 優先度付き改善案

### P0: 会計取消時に固定済み時間料金を無効化する

`cancel_checkout` で `store_slip_pricing_lines` と `source_type='automatic_pricing'` の注文行を無効化してから伝票を `open` に戻す。`release_checkout_ready` と同じ不変条件を共通Moduleに集約し、取消後スナップショットで二重計上がないことをSQLテストする。

### P0: 時間料金の適用開始ルールを決める

推奨は「営業日開始後の変更は次営業日から」である。少なくとも伝票開始時に料金プランの版と計算パラメータを固定し、既存営業中伝票が後から遡及変更されないInterfaceにする。

別案として時刻指定の料金プラン履歴を持つ場合は、各料金イベントの発生時刻に有効だった版を使う。現在の同一行上書きでは旧設定を復元できない。

### P1: 履歴表示に必要な名称をスナップショットする

最低限、伝票に卓番コード・卓番名、指名行にキャスト名・所属店舗名・指名表示名を保存する。金額だけでなく「当時誰に、どの卓番で、どの名目だったか」を履歴として固定する。

マスタ名称の訂正を過去へ反映したい要件がある場合は、無条件な現在値JOINではなく、訂正履歴と表示ルールを明示する。

### P1: 商品を物理削除せず論理無効化する

注文行のスナップショットで会計額は守られているが、物理削除で商品種別・カテゴリ・商品別集計の参照が失われる。`is_active=false` を標準にし、物理削除は未参照データだけに限定する。

### P1: キャスト売上配分設定を営業日単位で固定する

配分基準と方式を営業日開始時、または最初の配分保存時に固定する。一括確認は保存済み行の `source_amount_type`、`split_mode`、`base_amount` を無条件に書き換えない。

### P2: マスタ更新のSeamで反映時点と履歴方針を統一する

各保存Interfaceの結果に次を明示する。

- 適用対象: 新規取引のみ、営業中全件、次営業日
- 履歴: 金額固定、名称固定、現在名称表示
- 反映: 即時、次回同期、画面再読込
- 無効化時: 既存伝票の編集可否

この判断をPageModel、JavaScript、Repository、SQLへ分散させず、マスタ変更ModuleのInterfaceとして持たせる。

### P2: キャッシュへ版または期限を導入する

店舗・卓番・商品・キャスト・指名・店舗コンテキストに更新版を持たせるか、短いTTLを設定する。複数Azureインスタンスと複数ブラウザを考慮し、プロセス内キャッシュ削除だけに依存しない。

### P2: 支払方法と勘定科目をサーバー由来にする

支払方法は有効マスタから画面を生成する。領収書の勘定科目・補助科目もマスタID・コードを選択し、仕訳保存処理が名称スナップショットを作る契約を実DBと統一する。

## 修正までの暫定運用

- 時間料金、商品価格、各バック単価、指名種別、配分設定は営業中・締め作業中に変更しない。
- マスタ変更は営業日を締めた後、次の営業日を開く前に行う。
- 営業中伝票がある商品・カテゴリ・キャストは削除・無効化しない。
- SQLで直接変更した後は、Azure App Serviceの全インスタンスでキャッシュが更新されたことを確認し、営業端末と注文端末の画面を再読込する。
- 料金変更前後の伝票、会計取消、再会計、キャスト売上配分、領収書再印刷を本番相当データで確認する。

## 必須回帰シナリオ

1. 旧商品価格で注文後に価格変更し、既存行が旧価格、新規行が新価格になること。
2. 旧バック単価で注文後に単価変更し、既存バック額が変わらないこと。
3. 営業中に料金プランを変更した場合の期待仕様を決め、その仕様どおりになること。
4. 会計伝票発行後に料金変更し、会計準備中の金額が変わらないこと。
5. 会計確定、取消、再会計で時間料金が一度だけ計上されること。
6. キャスト削除後も過去の勤怠、指名、バック、売上配分を表示できること。
7. 卓番・キャスト・指名表示名変更後も、採用した履歴方針どおりの名称になること。
8. 配分設定変更後に保存済み配分を一括確認しても、算定経緯のメタデータが変質しないこと。
9. 支払方法無効化が全端末へ反映され、選択後のRPCエラーにならないこと。
10. 勘定科目名変更後も、過去仕訳の名称スナップショットが維持されること。
