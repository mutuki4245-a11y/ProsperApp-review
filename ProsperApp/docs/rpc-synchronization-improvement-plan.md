# 同期改善計画

この文書は、画面操作別のRPC棚卸しを順にレビューしながら、合意した同期方針を記録する作業文書である。実装・SQL適用・コミットは、この文書の採否が確定するまで行わない。

## 実装反映状況（2026-08-07）

本書の採用事項はすべて実装済みである。共通 `SyncStore`、shell-first GET、領域別revision、single-flight、`operation_id` 冪等性、browser draft/outbox、mutation応答による画面再構成を全対象画面へ適用した。

- 営業中、注文、会計、勤怠、締め、納品額、キャスト売上額、ドリンクバック、領収書、管理masterを各current/v2 read・mutation契約へ切り替えた。
- 成功mutation直後のredirect GETと同画面全体readを通常経路から除去した。
- 結果不明時は同じcommandと `operation_id` を保持し、確定した業務エラーだけを再編集可能状態へ移す。
- 旧Razor handler、旧Repository/DTO、旧Edge allowlist、旧RPC定義を削除した。互換fallbackと二重writeはない。
- 旧シャンパンバック物理モデルは既存行を新ドリンクバック調整へ一方向移行してから削除する。

SQL適用順、非可逆移行、確認手順は `HANDOFF.md` と `Sql/store_rpc_functions.sql` を正とする。2026-08-07に対象SupabaseへSQLを適用し、`prosper-rpc` v39をdeployしてv2 bootstrapの実呼び出しまで確認した。

## 0. 横断実装契約

以下を全画面の共通前提として固定する。この節に反する画面固有の記述は、この節を優先する。

### 画面・通信の境界

- Razor GETは認可確認と状態非依存のshellを返す。browser cacheだけにあるデータをRazor HTMLへ埋め直すことはしない。
- 対象画面はbrowserの `SyncStore` がJSON read/mutation応答を保持してhydrateする。通常成功経路でRazor POST、redirect GET、成功後の全体再読取を使わない。
- 既存Razor画面からの移行は画面単位で完結させる。移行済み画面に旧handlerへのfallbackを残さず、未移行画面だけが旧契約を利用する。

### cacheの責務

| 層 | 保持するもの | 正しさ上の位置付け |
|---|---|---|
| App Service process cache | bootstrap/masterの短期複製 | RPC削減のみ。miss・複数インスタンスを前提にする |
| browser `SyncStore` | 画面shell用master、管理master、直近のread model | 即時描画用。revision付き応答でのみ確定更新する |
| runtime snapshot | 営業日、伝票、勤怠、締め状態、領収書キュー | master cacheに混ぜない。対応するcurrent read/mutation応答だけが更新する |
| browser outbox | 領収書など未確定command | UIを止めないための永続一時状態。サーバーconfirmed前は確定状態に混ぜない |

browser cacheを使って0 RPC遷移にする画面は、必ずshell + browser hydrateへ移す。App Service cache hitだけを、利用者端末での即時表示の根拠にしない。

### revision・冪等性・認可

- 営業中/勤怠/締めの状態変更は `business_day_id + business_day_revision` と、必要なら対象伝票・対象勤怠行のversionを検証する。
- 管理masterは全体revisionではなく領域別revision（卓、キャスト、スタッフ、商品、指名、料金）を検証する。
- 領収書は全キューrevisionを保存可否の条件にせず、対象documentのpending versionを表すtokenだけを検証する。
- すべてのmutationは `operation_id` を受け、同じIDの再送に同じ確定結果を返す。領収書outboxを含むクライアント再送に備え、operation結果は少なくとも30日保持する。
- 部署ID、営業日、対象ID、管理者権限、締めoverride権限はDB/RPC側で認可・整合性を検証する。browserが持つcache、feature flag、revisionは認可根拠にしない。

### 後方互換を持たない切替

新しいv2 read/mutation契約は旧契約を置き換える。旧newの二重write、旧payload変換、画面内fallbackは作らない。DB/RPCと移行済み画面は同じリリース単位で切替え、切替後に不要な旧handler/RPC/DTOを削除する。既存データがある物理モデルは、一方向のデータ移行を行ってから旧モデルを廃止する。これは旧クライアント互換を維持することとは別である。

## 判断の原則

- 画面が即時描画に必要とする静的な参照データ（マスタ）と、必ず最新性が必要な稼働中データ（営業日・伝票snapshot）を同じキャッシュ契約にしない。
- 呼出側が `current business day`、`snapshot`、個別のマスタ取得順を知る必要がないよう、画面用の深いモジュールを置く。
- 書込みの成功応答は、次の画面表示に必要な確定状態または差分を返す。成功直後の「念のため再取得」を通常経路にしない。
- 回数だけでなく、古い営業日・古い伝票を表示しないこと、再送で二重更新しないことを受入条件にする。

## 1. 営業中トップ `/` のGETと初期同期

### 現状

- `IndexModel.OnGetAsync` は `LoadPageAsync` を呼ぶ。
- `LoadPageAsync` は `GetBusinessHomeBootstrapAsync` を呼び、同メソッドは常に `GetStoreBootstrapAsync` → `store.get_store_bootstrap` を実行する。既にbootstrap payload cacheがあってもトップGETでは読まない。
- その応答は店舗context、卓、指名、商品、出勤キャスト、決済、営業日、営業中snapshotに加え、管理画面向けマスタも含み、個別cacheを水和する。
- その後の営業中一覧は、ブラウザが10秒ごと・focus/visible/online・会計後に `current business day` と `get_business_day_snapshot` を取得する。

根拠: `Pages/Index.cshtml.cs:138-148,425-448`、`Features/BusinessHome/BusinessHomeApplicationService.cs:46-110`、`Infrastructure/Supabase/SupabaseStoreSlipRepository.cs:125-203`、`Infrastructure/Supabase/SupabaseStoreMasterBootstrapper.cs:21-82`。

### 暫定判断

**方向性は採用候補とする。**

営業中トップの通常GETは、マスタcacheが揃っていれば **Supabase RPCなしで画面shellを返し**、ブラウザが直後に **1回だけ現在営業日を解決した最新snapshotを取得する** 形がよい。

#### 合意事項: 自動更新時のcurrent business day解決

営業中一覧の自動更新では、`current business day` をアプリ側で先に別RPC取得しない。snapshot RPCが同じDB処理の中で現在営業日を解決し、その営業日に対応するsnapshotとrevisionを返す。

- 理由: `current` と `snapshot` の間に営業日開始・終了が起きても、異なる営業日を組み合わせないため。
- 効果: current cache miss時の2往復を1往復へ削減する。
- UI契約: `has_business_day=false`、`business_day=null`、空snapshotを正常応答として扱う。営業日がないことをエラーにしない。
- 実装制約: current business dayの特定とsnapshot構築は、同一RPC・一貫したread条件で行う。応答には `business_day_id` と `business_day_revision` を必ず含める。

ただし「cacheがあればRPCなしで遷移 → `get_snapshot`」は、画面遷移のサーバーGETが0回であっても、snapshot取得自体は1回のRPCである。目標表現は次のとおりにする。

| 状態 | サーバーGET中のRPC | 初期同期 | 合計 | 表示方針 |
|---|---:|---:|---:|---|
| マスタcache warm | 0 | 現在営業日+snapshotを1回 | 1 | shellを即時表示し、伝票領域だけ同期中にする |
| マスタcache cold | bootstrap 1 | 原則0（bootstrap応答のfresh snapshotを使う） | 1 | shellとsnapshotを同時に初期表示する |
| cold応答をshell専用にした場合 | bootstrap 1 | 現在営業日+snapshotを1回 | 2 | 採用しない。初回体験を悪化させる |

### 目標モジュールとinterface

`BusinessHomeApplicationService` の外側に、画面が知る必要のない取得順・cache判定を隠す **BusinessHomeSynchronization モジュール** を置く。Razor PageとJavaScriptが知るinterfaceは次の2つに絞る。

1. `LoadShellAsync()`
   - マスタcacheがwarmならDB RPCを発生させず、context、卓、指名、商品、決済などの画面shellだけを返す。
   - cache missなら、部署単位のsingle-flightでbootstrapを1回だけ実行し、マスタcacheを水和してからshellを返す。
   - runtimeの営業日やsnapshotを、cacheされたbootstrap payloadから「最新」として返さない。

2. `GetCurrentSnapshotAsync(knownRevision)`
   - RPC 1回で現在の営業日をDB側で解決し、営業日なしも含めてsnapshotと `businessDayRevision` を返す。
   - 現在の `GetSnapshotAsync` のように、アプリ側で `get_current_business_day` を取得してから `get_business_day_snapshot` を呼ぶ二段階を公開しない。
   - 将来、`knownRevision` により変更なしを返せるようにするが、これはpayload/SQL削減であってRPC回数削減とは区別する。

このseamを置くことで、PageModelは「masterがあるか」「営業日IDを先に取得するか」「cache TTLは何秒か」を知る必要がなくなる。実装内では既存repositoryを使ってもよいが、画面は2つのinterfaceだけを利用する。

### RPC契約案

#### 初回bootstrap（cold時のみ）

- 推奨名: `store.get_business_home_bootstrap_v2`
- 返却: shell用master + **このリクエスト時点で作成した** current business day / snapshot + revision。
- 現行 `get_store_bootstrap` の管理画面専用フィールド（部署一覧、各管理一覧、商品管理catalog、料金planなど）は含めない。
- 同じプロセスのmaster cacheがwarmなら呼ばない。

#### 通常同期

- 推奨名: `store.get_current_business_home_snapshot`
- 入力: `p_department_id`、任意の `p_known_revision`。
- 返却: `has_business_day`、`business_day`、`snapshot`、`business_day_revision`、必要なら `unchanged`。
- 営業日IDは入力にしない。DB側で現在営業日を決めることで、営業日切替・終了とクライアントのraceを同じread model内に閉じ込める。

### UI状態遷移

```
Razor GET
  ├─ shell cache hit  → shellを描画（伝票は同期中）
  │                     └─ browser: get_current_business_home_snapshot 1回
  └─ shell cache miss → bootstrap v2 1回でshell + fresh snapshotを描画

以後
  ├─ 書込み成功       → 書込みRPCのsnapshot/deltaを適用（追加fetchなし）
  ├─ 他端末の変更      → 定期同期または将来のpushでsnapshotを取得
  └─ 営業日なし        → 同じsnapshot契約で空状態を描画
```

### cache境界と無効化

- **マスタcacheに含める:** context、卓、指名マスタ、商品、決済方法。マスタ更新の成功応答で当該キーを直接更新するか、当該キーだけを削除する。
- **マスタcacheに含めない:** current business day、営業中snapshot、出勤状態、会計状態、未保存編集。これらをbootstrap payloadの残りから再利用しない。
- **runtime state:** `get_current_business_home_snapshot` が唯一の通常読取経路。応答revisionで古い非同期応答を破棄する。
- App Serviceが複数台になる場合も、master cache hitを正しさの前提にしない。cache miss時にbootstrapへ戻れること、書込み応答で当該画面を確定できることを保つ。

#### 合意事項: 伝票作成modalの出勤キャスト候補

出勤キャスト候補は `cast_master` の静的マスタではない。現在営業日と勤怠から導かれる **営業中snapshotに従属した派生参照データ** として扱う。

- 新しい `get_current_business_home_snapshot` の応答に `attendance_casts` を含める。候補取得専用の `get_order_attending_casts` をmodal open時に通常呼ばない。
- ブラウザは `department_id + business_day_id + business_day_revision` に対応する候補だけを保持する。次のsnapshotが同revisionなら再利用し、revisionが変われば応答の候補で置換する。
- キャスト/スタッフの勤怠保存、営業日開始・終了、キャストの無効化/削除など、候補に影響する書込み成功応答は、最新revisionと候補を返すか、次のsnapshot同期を必ず完了させる。
- サーバー側の30秒runtime cacheは最適化として残してよいが、正しさの根拠にはしない。無期限master cacheへ昇格させない。
- modal open時は、直近snapshotの候補を即時表示する。同期中または候補のbusiness day IDが現在表示中のIDと異なる場合だけ、進行中のsnapshot同期を待つ。modal専用の追加RPCは発生させない。

これにより、候補一覧は操作感としてマスタ同様に即時表示されるが、古い出勤状態を使って伝票作成することは防げる。

### 受入条件

- master cache warm時、`/` のRazor GETからEdge Functionへの呼出が0回である。
- warm時、初期伝票同期は `get_current_business_home_snapshot` 1回だけである。
- cold時、bootstrapとsnapshotが二重に実行されず、合計1 RPCで初期画面が表示できる。
- 新しい営業日開始・営業日終了・別端末会計後でも、古いbusiness day IDでsnapshotを要求してエラーにしない。
- 10秒poll、focus、onlineが同時に起きてもsingle-flightにより同時snapshot要求を1本にする。
- 既存の未送信編集ドラフトは、revisionの後退したsnapshotで失われない。

### 実装決定

- warm GETはruntime cacheを確定表示せず、伝票領域を同期中表示にする。
- cold GETは `get_business_home_bootstrap_v2` でmasterと同時点snapshotを1回で返す。旧bootstrapを流用しない。
- revision変更なしはHTTP 200の小JSONで返し、ETag/304は使わない。

## 次に確認する画面

## 2. 営業中トップの伝票作成POST

### 合意事項

伝票作成は独立したRazor POST・前後bootstrapではなく、営業中画面が持つ変更キューの操作種別に取り込む。操作成功の応答に最新snapshotを含めれば、作成後に別途一覧を読み直す必要はない。

通常経路は次の1往復とする。

```
最新snapshot + shell上の入力
  → create_slip 操作を変更キューへ追加
  → 即時 flush（1 RPC）
  → operation result（作成したslip ID）+ 最新snapshot + revision
  → 一覧・出勤キャスト候補・件数を同時に更新
```

これにより、現行の「POST前 `LoadAsync` → `EnsureCurrentAsync` → `create_slip` → POST後 `LoadAsync`」を置き換える。既存営業日で3〜4回、営業日なしで最大5回のRPCを、作成操作の通常経路では1回にする。

### 既存flushを深くするためのinterface

現行 `flush_business_home_changes` は `slip_id > 0` を前提にするため、新規伝票をそのまま投入できない。後継 `sync_business_home_changes_v2` を作り、現行RPCは拡張しない。移行済み営業中画面はv2だけを呼び、旧handler/payloadへのfallbackは持たない。

`create_slip` は `slip_id = null` と `client_draft_id` を持つ独立したoperation typeにする。既存編集操作と新規作成の入力不変条件を混ぜず、`slip_id` の意味を曖昧にしない。

操作入力は、伝票作成に必要な差分だけにする。

- `operation_id` / `client_batch_id` / `client_draft_id`（再送時のidempotencyと、作成後slip IDの対応付け）
- `expected_business_day_id` と `expected_business_day_revision`
- `table_id`、`opened_at`、顧客ラベル、指名キャスト/種別/金額、memo

snapshot全体をサーバーへ送り返さない。クライアントが持つ最新snapshotは入力支援・即時検証・楽観表示のために使い、DB側が現在の営業日、卓、キャスト、指名マスタ、出勤状態、closed day制約を正として再検証する。

### 応答契約

成功時に少なくとも次を返す。

- `operation_id` / `client_draft_id` / `slip_id`
- `business_day_id` / `business_day_revision`
- 作成済み伝票を含む最新snapshot
- UI表示用の操作結果（作成成功、警告など）

同じ `client_batch_id` の再送では、二重に伝票を作成せず、最初に確定した `slip_id` とsnapshotを返す。revisionが競合した場合は、DBの業務上安全な再検証を通せる操作だけを適用し、それ以外は現在snapshot付きのconflictを返す。

### 制約

- 伝票作成可否に必要な出勤キャスト候補は前節のsnapshot内 `attendance_casts` を使う。ただしサーバーは保存時に必ず勤怠を再検証する。
- 営業日なしのときに「出勤キャスト必須」の現行ルールを維持するなら、通常の伝票作成は失敗し、勤怠入力へ誘導する。伝票作成が営業日を自動開始する現行仕様を残す場合も、出勤状態を含む開始処理を同一トランザクションへ明示的に設計する必要がある。
- 作成ボタンではdebounce待ちをせず、キューへ追加後に即時flushする。他の未保存編集があれば同じbatchに同梱し、失敗時はローカルドラフトを残す。

## 次に確認する画面

## 3. 営業中編集のflushと変更キュー

### 暫定判断

**変更キューにコマンドを積み、一括送信する外側の同期モデルは採用する。**

これは画面全体のsnapshotを上書き保存する方式ではない。顧客追加/離席、指名追加/取消、調整追加/取消、注文数量変更/取消、カラオケ数量変更などの「何をしたか」をコマンドとして送る。サーバーは順序を保って検証・適用し、最後に1つの確定snapshotを返す。

現行実装も、ブラウザの `pendingOperations` と `pendingFlushBatch` を使い、変更を配列として `flush_business_home_changes` へ一括POSTしている。この外側の形は維持する。

### 目標となる同期契約

```
UI操作
  → command queueへ追加（operation_idを発行）
  → 同一操作単位でまとめてflush
  → sync RPCが現在営業日・revisionを内部で解決
  → コマンドを入力順に検証・原子的に適用
  → operation results + 最終snapshot 1個 + 新revisionを返す
  → browserが最終snapshotだけを表示状態として採用
```

入力は少なくとも次を持つ。

- `client_batch_id` と各 `operation_id`（通信失敗時の再送を同じ結果に収束させる）
- `expected_business_day_id` と `expected_business_day_revision`
- 操作種別とその差分payload

同期RPCは、前節の `get_current_business_home_snapshot` と同様に現在営業日を内部で解決する。これにより、flush前の `get_current_business_day` を通常経路から外し、1バッチを1RPCにする。

### キューの扱い

- **順序を保持する:** 同じ伝票に対する「顧客追加 → 指名追加」のように意味が変わる操作は並び替えない。
- **安全なものだけ併合する:** まだ未送信の同一行に対する最終数量や表示ラベルなど、業務的に最後の値だけで等価な操作はクライアント側で併合できる。追加・取消・会計に関わる操作を一般的なlast-write-winsにしない。
- **即時性を保つ:** 現在のように短時間でflushしてよい。入力中の操作を長く待たせるためだけの大きなdebounceは入れない。画面遷移・会計・明示保存の直前は必ず `flushNow` で完了を待つ。
- **失敗時は残す:** batchが未確定ならローカルドラフトを消さず、同じ `client_batch_id` で再送できる状態にする。成功済みbatchの再送は新しい書込みを起こさない。
- **競合時は再取得ではなく結果を返す:** revision不一致や業務制約違反は、現在snapshotと操作ごとのconflict理由を返す。クライアントは未適用コマンドを利用者に示して再編集できるようにする。

### 現行flushで直す点

- 現在の `flush_business_home_changes` はHTTPとしては1回でよい。
- ただしSQL内部では、batch中の各editor operationで `apply_business_slip_editor_operation` がsnapshotを生成し、flushの最後にもsnapshotを生成する。クライアントが使うのは最後のsnapshotだけなので、中間操作はoperation resultだけを返すようにする。
- 伝票作成を加える場合は、前節のとおり既存RPCの `slip_id > 0` 前提を曖昧にせず、後継の `sync_business_home_changes_v2` に新規作成コマンドを持たせる。
- 会計発行・確定・取消は、帳票/決済/会計snapshotの不変条件が強いため、同じ一般編集queueには入れない。各会計mutationは独立した原子的RPCとし、成功応答へ営業中snapshot/deltaを付ける。

### 受入条件

- 通常の営業中編集は、current business dayの事前読取を含めず、1 batch = Edge RPC 1回である。
- 同じbatchの再送で、顧客・注文・調整・伝票が二重に作成されない。
- 複数操作の成功応答は最終snapshotを1個だけ含む。操作数Nに比例する中間snapshotを返さない。
- 別端末の更新が競合しても、他端末の確定更新をクライアントが上書きしない。

## 次に確認する画面

## 4. 会計伝票発行

### 合意事項

会計伝票発行は、一般編集のflushが**確定済みであること**を前提とする独立した原子的RPCにする。発行RPCは、伝票を会計準備状態へ変更し、会計伝票/確認用データを作り、最新営業中snapshot（または対象伝票delta）とrevisionを同じ応答で返す。

```
未保存編集あり
  → flushNow が成功するまで待つ（1 RPC）
  → issue_checkout_statement_v2（1 RPC）

未保存編集なし
  → issue_checkout_statement_v2（1 RPC）
```

したがって、発行後に一覧同期のための `get_business_day_snapshot` は不要になる。未保存編集がなければ、利用者の発行操作は1 RPCだけで完了する。

### `issue_checkout_statement_v2` の応答契約

- `slip_id`、`checkout_id`、会計伝票のprint data、review data
- `business_day_id`、`business_day_revision`
- 発行後の最新snapshot、または既存画面が安全に適用できる対象伝票delta
- 重複発行・状態競合・未保存変更・closed dayを区別したエラーコード

入力には `expected_business_day_revision` と、必要なら対象伝票の更新versionを含める。DB側は会計対象伝票をロックまたは同等の競合検証を行い、flush完了後に別端末が変更した古い状態で会計伝票を発行しない。

### 分離を保つ理由

- 会計伝票発行は、料金確定、会計snapshot、帳票データという会計上の不変条件を持つ。一般編集コマンドの一種として扱わない。
- `flushNow` は編集を先に確定するための同期点である。単にクライアントのpromiseを待つだけでなく、サーバーの成功結果を確認してから発行する。
- 未flush編集を発行RPCに同梱して合計1 RPCにする案は将来の最適化候補だが、編集と会計の失敗/再送/監査を同一トランザクションにする高リスク変更となる。最初の段階では採用しない。

### UI状態遷移

1. 発行ボタンを押すと、未保存編集があれば `flushNow` を実行し、成功するまで会計操作を開始しない。
2. `issue_checkout_statement_v2` の送信中は、対象伝票の編集をUI上だけでなくサーバー側でも拒否する。
3. 成功応答のsnapshot/deltaを適用して一覧を更新し、返却されたprint dataで印刷する。追加の一覧fetchは行わない。
4. 失敗時は、確定済みのsnapshotと理由を表示し、印刷・会計準備状態を楽観的に変更しない。

### 会計準備解除・会計確定・会計取消

これらも発行と同じ原則に揃える。各操作は一般編集queueには入れず、対象伝票の状態遷移・会計データ変更・結果snapshot/delta返却を1つの原子的RPCで行う。

| 操作 | 推奨RPC | DB側で確定する主な状態 | 成功応答 | 通常の追加flush |
|---|---|---|---|---:|
| 会計準備解除 | `release_checkout_ready_v2` | `checkout_ready` から編集可能状態へ戻す、必要な会計準備データを無効化 | `slip_id`、revision、最新snapshot/delta | なし |
| 会計確定 | `confirm_checkout_v2` | 決済内訳、受取額/釣銭、checkout、会計snapshot、伝票の会計済み状態 | `checkout_id`、釣銭、領収書print data、revision、最新snapshot/delta | なし |
| 会計取消 | `cancel_checkout_v2` | checkout/payment/伝票状態を業務ルールに従って取消・復元 | `checkout_id`、revision、最新snapshot/delta | なし |

- 発行時点で一般編集はflush済みであり、`checkout_ready` 以降はサーバーも通常編集を拒否する。そのため解除・確定・取消の前に一般編集flushを追加しない。
- 各入力には対象伝票IDと期待revision/versionを含める。すでに別端末で解除・確定・取消された場合は、同じmutationを二重適用せず、現在状態を含むconflictを返す。
- `confirm_checkout_v2` は現行どおり領収書print dataを同じ応答に含める。同一セッションで保持済みなら再印刷にread RPCは不要であり、保持済みデータがない復旧時だけ専用read RPCを1回呼ぶ。
- JSの `prosper:business-slips-refresh` による成功直後の一覧fetchは、各v2応答のsnapshot/delta適用へ置き換える。

### 会計伝票・領収書の再印刷

この計画でいうsnapshotには、用途の異なる2種類がある。両者を混同しない。

| 種類 | 用途 | 更新性 | 主な内容 |
|---|---|---|---|
| **営業中snapshot** | 営業中一覧と編集UI | 伝票・注文・会計状態の変更ごとに更新 | 卓、伝票、顧客/キャスト、注文、表示用金額、状態、`business_day_revision` |
| **会計帳票データ** | 会計伝票・領収書の印刷 | 発行/確定時点の会計事実を正本として復元 | 税額、料金明細、決済内訳、会計日時、会計ID、発行者、帳票用整形データ、帳票version |

#### 訂正: 会計済み伝票は一覧から外さない

現行の `store.get_business_day_snapshot` は `checked_out` の伝票も `slips` に残し、一覧の `checkedOutSlipCount` にも数える。会計後は、保存済みの `business_home_data` を表示用の基礎として使い、`status=checked_out`、表示文言、badge を上書きする実装である。したがって、営業中snapshotは会計済みに変わった一覧を描画するには十分であり、「会計済みだから一覧対象から外れる」という前提は置かない。

ただし、**現在の営業中snapshotだけ** は帳票を完全には描画できない。会計伝票には最終的な小計・サービス料・税額・帳票用の並び順などがあり、領収書には決済内訳と確定時に保存した発行者情報がある。これらは営業中一覧のpayloadには含まれていない。会計伝票は発行時の `store_slip_accounting_snapshots.print_data` を正本とし、領収書は確定済みcheckout・決済・発行者snapshotから復元する。

これは「同じ状態なら二つのRPCが必要」という意味ではない。状態変更にはRPCが必要で、その**同じmutationの応答**が、一覧更新用の `business_snapshot` / `business_delta` と、即時印刷用の帳票データを一緒に返せばよい。直後の印刷・同一セッションでの再印刷に追加read RPCは不要である。

したがって、会計mutationの応答には用途別に両方を含める。

- `business_snapshot` または `business_delta`: `checked_out` を含む営業中一覧を追加fetchなしで更新するため。
- `statement_print_data` / `receipt_print_data`: その場で印刷・同一セッションで再印刷するため。会計伝票は保存済み帳票データ、領収書は確定済み会計データから構成する。

再印刷用のRPCは、印刷回数を管理するためのものではない。ページ再読込、ブラウザ/端末変更、会計準備状態の復旧後に、DB上の会計帳票データから正本となる帳票データを復元するためのreadである。

- 会計伝票発行の成功応答にはstatement print dataが含まれる。現行JSも同一セッション中はこれを会計キューに保持し、直後の再印刷はRPCを送らない。
- 会計確定の成功応答にはreceipt print dataが含まれる。同一表示状態での再印刷は、この確定済みデータをメモリ上で使える。
- `get_checkout_statement_print_data` / `get_checkout_receipt_print_data` が必要なのは、保持済みデータがない状態での復旧・再表示・明示再印刷である。この時だけ1 read RPCを許容する。

帳票データは会計帳票versionまたはcheckout IDに紐付ける。取消・解除・対象伝票の状態遷移を受けたらブラウザ側の帳票cacheを破棄し、復旧時は必ずDBから再取得する。長期のlocalStorage保存は帳票内容の機微性と端末共有リスクを評価してからにし、正しさの根拠にはしない。

## 5. 注文画面 GET（`/Orders`）

### 現状

注文画面のRazor GETは、トップの**現行実装とは同じではなく、マスタ取得だけは目標形に近い**。

- `OrderEntryApplicationService.LoadPageAsync` は最初に `IStoreMasterBootstrapper.EnsureAsync` を呼ぶ。bootstrap payload cacheがwarmなら `get_store_bootstrap` を再実行しない。ここは、現状のトップが毎回 `GetStoreBootstrapAsync` を呼ぶ点よりよい。
- `store_context` と商品はmaster cacheから取得する。現在営業日と出勤キャスト候補は30秒のruntime cacheから取得する。runtime cacheが切れていれば `get_current_business_day`、続いて `get_order_attending_casts` が起きるため、GETだけでも0〜2 RPCになり得る。
- bootstrap cacheがcoldなら、現行bootstrapがcontext・商品・現在営業日・出勤キャスト候補を水和するので、Razor GET自体は通常 `get_store_bootstrap` の1 RPCで済む。
- ただしGETは注文対象伝票をHTMLへ入れず、`Slips=[]` で返す。ブラウザの `order-entry.js` が表示直後、以後10秒ごと、focus/visible時に `?handler=SlipOptions` を呼ぶ。このhandlerは `get_current_business_day`（runtime cache miss時のみ）→ `get_order_entry_slips` を実行する。

従って、master/runtime cacheがwarmな**Razor GET単体**は0 RPCでも、利用者が伝票候補を選べる状態になるまでの**初回画面遷移全体**では通常 `get_order_entry_slips` が1 RPC必要である。現在営業日のruntime cacheも切れていれば2 RPCである。

### 方針

注文画面もトップと同じ考え方、すなわち「静的shellはcache、動的業務データは画面用の読取1回」に揃える。ただし、ここで必要なのは営業中一覧のsnapshotではなく、**注文先を選ぶための候補同期**である。営業中一覧の全payloadを流用しない。

```text
master cache warm
  Razor GET: shell（context・商品）を0 RPCで返す
  browser: get_current_order_entry_candidates 1回
    = 現在営業日の解決 + open伝票候補 + 出勤キャスト候補 + revision

master cache cold
  order-entry bootstrap v2 1回
    = shell + 同時点のorder-entry candidates
```

- 現行 `get_order_entry_slips` が返すのは、`status='open'` の伝票の `slip_id`、卓表示、開店時刻、顧客数/名前、指名キャスト、memoだけである。注文明細、金額、会計情報、営業中一覧全体は10秒同期に含めない。
- `get_current_order_entry_candidates` は営業日IDを入力に取らず、DB内で現在営業日を解決する。営業日なしなら空の伝票候補・候補キャストを正常応答にする。
- 応答には `business_day_id`、`business_day_revision`、`open_slips`、`attendance_casts` を含める。商品はmaster shellに残す。
- これにより、現在の `get_current_business_day` と `get_order_entry_slips` の二段階、さらに出勤候補の別cache依存を画面契約から外す。
- 10秒poll、focus、visible、手動更新は同じsingle-flight候補取得を呼ぶ。更新がなければ `unchanged` を返せるようにする。

トップと共有すべきなのは「現在営業日をsnapshot RPC内部で解決する」基盤とrevision規約であり、返す業務データは画面別に小さく保つ。注文画面へ営業中一覧の会計・料金・全明細まで送らない。

### 受入条件

- master cache warm時、`/Orders` のRazor GETはSupabase RPC 0回である。
- 初期表示、10秒更新、focus、visible、手動更新の注文候補同期は、それぞれ最大1 RPCである。poll payloadは伝票候補と出勤キャスト候補だけであり、注文明細や会計情報を含まない。
- 現在営業日のruntime cache missによって、注文候補同期が `current` + `slips` の2 RPCにならない。
- 営業日切替中でも、古いbusiness day IDの伝票候補や出勤キャスト候補を組み合わせて表示しない。

## 6. 注文登録 POST

### 現状

現在のRazor POSTは次の順で動く。

```text
注文キューを復元
  → LoadOptionsAsync（context/current/items/attendance を再読取またはcacheから取得）
  → ブラウザ側と同じ入力検証
  → store.add_order_lines（1 write RPC、応答は inserted_count のみ）
  → HTMLを再描画
  → ブラウザ初期化時に SlipOptions を再取得
```

`store.add_order_lines` は保存時に、対象伝票が `open` であることをlock付きで検証し、商品が有効・注文可能であること、バック対象キャストが対象営業日に出勤していることも再検証する。従って、POST前の `LoadOptionsAsync` は保存の正しさには不要で、主に入力エラー時の画面再描画と事前メッセージのためにある。

また現在のwrite応答は `inserted_count` だけであり、`operation_id` がない。そのため、通信断で成功応答だけを受け取れなかった再送を安全に判別できない。

### 合意候補: 成功時は保存RPC 1回とその応答だけ

**通常の注文登録では、保存用RPC 1回とその結果応答だけでよい。** 注文を足しても対象伝票が `open` のままであれば、注文画面の伝票候補リスト自体は変わらないため、成功直後に候補同期をもう一度行う必要はない。

```text
ブラウザの注文キュー
  → submit_order_entry_v2（1 RPC）
      DB内で現在営業日・伝票・商品・勤怠を検証して原子的に保存
  → operation results + 新revision
  → 成功したキュー行だけを消去し、成功表示
```

#### `submit_order_entry_v2` の入力

- `client_batch_id` / `operation_id` と、各行の `client_line_id`。再送しても同じ注文行を二重作成しない。
- 候補同期で得た `expected_business_day_id` と `expected_business_day_revision`。
- `slip_id`、`item_id`、数量、`cast_back_cast_id`。

サーバーは、クライアントの候補一覧を正とせず、現在営業日、対象伝票がその営業日の `open` であること、商品・出勤・バック計算を再検証する。通常の入力形式チェックはブラウザでも行ってよいが、事前にRPCで検証し直さない。

#### 成功応答

- `operation_id`、`inserted_count`、各 `client_line_id` に対応する `order_line_id` / `slip_id`。
- 確定後の `business_day_id` と `business_day_revision`。
- 画面に表示する警告または成功メッセージ。

成功した注文追加では伝票候補の全件を返さない。候補は変わらないため、pollの次回周期に通常同期すればよい。営業中トップを同一端末で開いている場合も、次回snapshot同期または将来のpushで注文表示を更新する。

#### 競合・失敗応答

別端末で会計準備・会計確定・取消などが起き、対象伝票が `open` でなくなった場合は、DBが保存せず `conflict` を返す。この失敗経路には、少なくとも無効になった `slip_id` と最新revisionを含める。候補表示を即時に正す必要がある場合だけ、現在の `open_slips` / `attendance_casts` を同じ失敗応答に含める。失敗のたびに別read RPCを強制しない。

### 実装上の分離

- 通常成功経路をHTML再描画型POSTではなく、JSONを返す非同期mutationにする。これにより、成功後の `LoadOptionsAsync` とJS初期化時の候補再取得を外せる。
- Razorの入力エラーを保ちたい場合は、基本的なJSON形式・数量の検証だけをクライアントで先に行う。サーバーの業務エラーはmutation応答の行別エラーとしてキューを残す。
- `add_order_lines` は移行済み注文画面から呼ばない。idempotencyとrich responseを持つ後継 `submit_order_entry_v2` へ置き換え、会計を含む営業中編集queueへは統合しない。

### 受入条件

- master/runtime cacheの状態にかかわらず、通常の注文登録はEdge write RPC 1回で完了する。
- 成功後、候補一覧取得・Razor再描画のための追加RPCを発生させない。
- 応答未達による同一 `operation_id` の再送で、注文行・キャストバックが二重作成されない。
- 対象伝票の会計状態が競合した場合、保存せず、キューを失わず、追加readなしで再選択に必要な情報を返す。

## 7. 勤怠 GET（`/Attendance`、`/Closing/Attendance`）

### 合意事項: トップと同じshell-firstにする

**その認識でよい。** 勤怠画面も、キャッシュできる名簿・設定は先にshellとして即時表示し、当日の勤怠だけを1回の動的同期で反映する。

| 区分 | shellに載せる/cacheするもの | 動的同期で取得するもの |
|---|---|---|
| 共通 | 店舗context、勤怠時刻刻み、キャスト/スタッフのID・表示名・所属・有効状態、時刻選択肢 | 現在営業日、各人のattendance ID、出退勤時刻、出勤状態、送迎利用、revision |
| 営業日なし | 名簿と入力枠、営業日開始の案内 | `has_business_day=false` |

時刻選択肢は店舗設定のminute stepから決定できるためshell側で組み立てる。出勤済みチェック、attendance ID、出退勤時刻は当日ごとに変わるためmaster cacheには入れない。

```text
master cache warm
  Razor GET: 名簿・時刻選択肢・空の編集shellを0 RPCで返す
  browser: get_current_attendance_editor_snapshot 1回
    = 現在営業日の解決 + 当日勤怠 + revision
  browser: 名簿と当日勤怠をperson keyでマージして行を確定表示

master cache cold
  attendance bootstrap v2 1回
    = shell + 同時点のattendance editor snapshot
```

### 現状との差分

現行 `AttendanceApplicationService.LoadAsync` は、`EnsureAsync` の後にcontext/current/casts/staffsを待ち、営業日がある場合はさらに `get_business_day_closing_attendance` を待ってから、サーバー側で名簿と勤怠をマージしてRazorの全行を生成する。

- master cacheがwarmでも、営業日がある限り `get_business_day_closing_attendance` は毎GETで発生する。
- current business dayの30秒runtime cacheが切れていれば、その前に `get_current_business_day` も発生する。
- よって、現行は「master cacheがある限り即時にshell表示」にはなっていない。

対象のsnapshot RPCでは現在営業日を同じDB処理で解決するため、`current` → `closing attendance` の二段階を公開しない。応答は `has_business_day`、`business_day_id`、`business_day_revision`、`attendance_entries` を返す。キャスト/スタッフの全名簿を繰り返し返さない。

### 表示と未保存編集の安全性

- 初期shellは名簿を表示してよいが、当日勤怠の取得完了前は既存の選択状態を「未出勤」として確定表示しない。行は同期中表示とし、保存ボタンを無効にする。
- snapshot取得後に、名簿と `attendance_entries` をperson keyでマージして入力欄を有効化する。営業日なしなら、空の状態を確定表示する。
- 勤怠入力中は10秒pollで入力を上書きしない。更新は初期表示、明示再取得、focus復帰時（未保存編集なし）のみとする。未保存編集がある場合は、保存・破棄・再読込の選択を要求する。
- 別端末の変更を検知した場合はrevisionを比較し、保存時に期待revision/versionを送る。古い画面からの保存で他者の勤怠を丸ごと上書きしない。

### 受入条件

- master cache warm時、勤怠ページのRazor GETはSupabase RPC 0回であり、名簿と時刻選択肢を即時表示する。
- 初期の当日勤怠同期は、現在営業日の事前読取を含めず1 RPCである。
- 同期完了前に、古いcacheの出勤状態で保存できない。
- 営業日開始・終了または別端末の勤怠変更があっても、別の営業日の勤怠と名簿を混ぜて表示しない。

## 8. 勤怠保存 POST

### 結論: 保存RPC 1回と、cache更新用の応答で完結させる

**そのとおり。** 勤怠保存は `save_attendance_editor_v2` のような1つの原子的RPCにし、応答の確定attendance snapshotでブラウザとruntime cacheを更新する。成功後に勤怠を読み直したり、master bootstrapを呼び直したりしない。

```text
編集済み勤怠コマンド + operation_id + expected versions
  → save_attendance_editor_v2（1 RPC / 1 transaction）
      現在営業日を解決（必要なら作成）
      キャスト・スタッフの出勤/取消/退勤/送迎を検証して保存
      確定attendance snapshotを構築
  → business day + attendance entries + attending casts + revision
  → UIとdynamic cacheへ適用（追加readなし）
```

### 現状が多い理由

`AttendanceApplicationService.SaveAsync` は、1つの利用者操作を次に分割している。

1. `get_current_business_day(forceRefresh=true)`
2. 営業日がなければ `EnsureCurrentAsync` 内で再度current取得し、`open_business_day`
3. `save_business_day_attendance`（キャスト出勤）
4. `save_business_day_staff_attendance`（スタッフ出勤）
5. `get_business_day_closing_attendance`（新規作成されたattendance IDを得るための中間読取）
6. `save_business_day_closing_attendance`（キャスト退勤）
7. `save_business_day_staff_closing_attendance`（スタッフ退勤）
8. redirect先GETによる再読取

キャスト・スタッフ・退勤の対象がある通常ケースでは、保存操作が複数のwriteと中間readに割れている。途中で失敗すると、キャスト保存だけ成功してスタッフ保存に失敗するような部分成功も起こり得る。

### `save_attendance_editor_v2` の責務

- DB内で現在営業日を解決し、営業日がなければ現在の業務日付で作成する。営業日開始時に最低1名のキャストを必須にするかは、現行の挙動を変える業務判断なので別途明文化する。
- `person_type` + `person_id` をキーに、キャスト/スタッフの選択、取消、出勤時刻、退勤時刻、送迎利用を一括で受け取る。クライアントが新規attendance IDを先に取得する必要はない。
- 所属・有効状態、時刻の妥当性、退勤が出勤より後であること、営業日状態を検証する。すべて成功した場合だけcommitする。
- `operation_id` で再送を冪等にする。全件を1 transactionで扱うため、競合/入力エラー時に一部だけ保存しない。
- 同じ人を別端末が変更していた場合に丸ごと上書きしないよう、`expected_business_day_revision` に加えて編集対象ごとのversionまたは更新時刻を検証する。

### 成功応答とcache更新

成功応答は、単なる保存件数ではなく次を返す。

- `operation_id`、作成/更新された `business_day`、`business_day_revision`
- 全員分または変更後に画面を再構成できる `attendance_entries`（attendance ID、person key、状態、出退勤、送迎）
- 注文画面・伝票作成で使う当日 `attending_casts`
- 保存件数・行別結果・表示用メッセージ

ブラウザはこの応答を勤怠editor snapshotとして即時適用する。アプリサーバー側でも、同じ応答で現在営業日と `OrderAttendingCasts` の**runtime cacheだけ**を置換または削除する。キャスト/スタッフ/店舗設定などのmaster cacheは勤怠保存では変わらないため、無効化しない。

複数App Service間のlocal cacheは正しさの根拠にせず、次の画面用snapshotがDBで確認する。書込み直後の同一画面は必ずこの成功応答を正本にする。

### 画面遷移

- `/Attendance` ではJSON応答を適用して画面に残り、再GETしない。
- `/Closing/Attendance` から締めトップへ進める場合は、勤怠保存のために勤怠を再読取しない。遷移先の締め画面は自身のshell/snapshot契約で読み込む。

### 受入条件

- 営業日あり/なし、キャスト/スタッフ混在、退勤入力ありのいずれでも、勤怠保存のDB往復は1 write RPCである。
- 保存成功後に `get_business_day_closing_attendance`、`get_current_business_day`、bootstrap、勤怠ページ再GETを追加実行しない。
- 通信断後の同一 `operation_id` の再送で、出勤・退勤・送迎状態が二重更新されない。
- 1行でも業務検証または競合に失敗した場合、部分保存せず、現在状態と行別エラーを返す。

## 9. 締めトップ GET（`/Closing`）

### 評価: 骨格はよいが、同期は1回にまとめる

現行は、締めの各パネルを個別に取得せず `get_business_day_closing_readiness` に集約している。締め可否、未会計伝票、酒代、勤怠、キャスト売上調整、現行名称のシャンパンバックを1つのreadinessで返しており、この方向は維持する。後述の名称変更後はドリンクバック調整として扱う。

ただし、初期表示と30秒/focus/visible更新では次の3 RPCになっている。

```text
GET: get_current_business_day（runtime cache miss時のみ）
browser Readiness: force get_current_business_day → get_business_day_closing_readiness
browser Receipts: get_pending_receipts
```

`get_business_day_closing_readiness` はSQL上すでに `pending_receipt_count` を計算できる。ただしアプリ層が `p_pending_receipt_status=null` を渡しているため、画面では別のReceipts endpointを呼んでいる。領収書は任意で締め可否には含めない、という業務ルールを保ったまま同一応答へ含められる。

### 方針

Razor GETは状態非依存の締めshellを返し、ブラウザが **1回だけ** `get_current_closing_dashboard` を呼ぶ。このRPCがDB内で現在営業日を解決し、readinessと任意の未入力領収書件数を同じread modelとして返す。

```text
Razor GET: パネル・日報枠・締め操作枠をshellとして表示（0 RPC）
  → browser: get_current_closing_dashboard 1回
      = 現在営業日 + readiness + pending_receipt_count + revision
  → パネル状態・締めボタン・BusinessDayIdを確定表示
```

- 応答: `has_business_day`、`business_day_id`、`business_day_revision`、open slip数、酒代状態、勤怠件数/未退勤数、売上調整状態、ドリンクバック調整状態、`pending_receipt_count`、`can_close`、`block_reasons`、`checked_at`。
- `pending_receipt_count` は領収書パネルの表示だけに使い、`can_close` と `block_reasons` の必須条件には加えない。
- 営業日なしも正常応答にする。初期shellに埋めたbusiness day IDとの不一致を警告する方式ではなく、毎回この応答全体を最新状態として適用する。
- 30秒poll、focus、visible、再取得はsingle-flightで同じ1 RPCを呼ぶ。現在の個別panel取得用コードは、readiness取得へ統一済みの部分を残して整理する。

### 日報は別read modelのまま

日報は印刷用を含む大きな集計payloadであり、締め可否の同期へ混ぜない。現在どおり別の `DailyReport` readとして扱う。日報を初期表示で必ず必要とするならdashboard同期と並列の1 RPC、操作時だけ必要なら遅延読込にする。いずれにせよclosing dashboard RPCへ統合しない。

### 受入条件

- 締めトップのRazor GETは、current business day cache missにも依存せず0 RPCでshellを返す。
- 締めパネルと任意の領収書件数の初期/定期同期は1 RPCである。
- 営業日切替時は、新しいdashboard応答をそのまま適用し、古いIDを前提に画面全体を再読込させない。
- 締めボタンの有効化はdashboard応答を表示するためだけに使い、実際の締めmutationはDB側で必ず同じ条件を再検証する。

## 10. 営業日締め POST

### 結論: 状態変更RPC 1回だけでよい

**そのとおり。** `close_business_day_v2` は、現在営業日の特定、締め条件の最終検証、営業日close、後続画面に必要な確定状態を1 transaction・1 RPCで行う。締めボタン押下前のdashboard readは操作可否を見せるためだけであり、保存前にもう一度current/readinessをアプリ層から読む必要はない。

現行の `ClosingApplicationService.CloseAsync` は、force current取得 → readiness取得 → `close_business_day` の順で動く。しかし最終の `close_business_day` SQLも内部で `get_business_day_closing_readiness` を実行し、条件未達ならcloseを拒否している。最終整合性の確認が二重になっている。

```text
expected_business_day_id + expected_revision + memo + operation_id
  → close_business_day_v2（1 RPC / 1 transaction）
      DB内で現在営業日をlockして解決
      締め条件を最終再検証
      closeを確定
  → closed business day + post-close dashboard state
```

### 応答契約

- 成功: `operation_id`、close済み `business_day_id` / `business_date`、`closed_at`、`has_business_day=false` のpost-close dashboard state、日報を開くための `report_business_day_id`。
- 条件未達/競合: closeしない。最新のreadiness、`block_reasons`、current `business_day_id` / revisionを同じ応答に含めるため、失敗直後に別read RPCを送らない。
- 同一 `operation_id` の再送: 二重closeせず、最初に確定した成功応答を返す。

締め後の日報本文は大きい別read modelなので、mutation応答には含めない。日報表示を続ける場合は、返却された `report_business_day_id` を使って日報の通常readを行う。

### 管理者override

通常の締めと同様に1 RPCで処理できる。ただし `ignore_closing_requirements` はクライアントのcheckboxだけを信用せず、RPC/Edge Function側で管理者権限を検証してから適用する。この認可条件は一般の状態変更最適化と混ぜず、現行以上に弱めない。

### 受入条件

- 通常の営業日締めは、事前current/readiness読取なしの1 write RPCである。
- 締め条件が直前に変化しても、DB側の同一transaction検証で不正なcloseを確定しない。
- 成功/失敗のどちらも、直後の締めdashboard更新に追加read RPCを必要としない。

## 11. 納品額（酒代）入力

### 締めトップのパネル取得には含める（現行readinessにも既に含まれる）

**含められるし、現行の `Readiness` 応答にも既に含まれている。** `get_business_day_closing_readiness` は `drink_delivery_amount` と `is_drink_delivery_amount_entered` を返しており、締めトップはこれを `drinkDeliveryAmount` / `isDrinkDeliveryAmountEntered` として受け取っている。

従って、前節の `get_current_closing_dashboard` では納品額パネル用の追加readを行わない。納品額、入力済み状態、営業日ID/revisionを同じdashboard応答から描画する。

### 納品額入力画面のGET

現行 `/Closing/DrinkCost` は `get_current_business_day` → `get_business_day_drink_delivery_status` を待ってRazor HTMLを返す。後者は営業日の `drink_delivery_amount` と `entered` flagだけを読むため、締めトップのdashboardと内容が重複する。

納品額は締めトップから開くこの画面でしか変更しない前提なので、**締めトップからの通常遷移ではbrowser stateを使い、専用readを送らない。** 通常のRazorページ遷移ではメモリ上のJavaScript stateが失われるため、dashboard応答の納品額fragmentを `department_id + business_day_id + revision` 付きで `sessionStorage`（または同等のページ遷移をまたぐbrowser state）に渡す。

```text
締めトップ dashboard応答
  → 納品額fragmentをbrowser stateへ保存
  → /Closing/DrinkCost のRazor GETは0 RPCで入力shellを返す
  → browserがfragmentを適用して金額・BusinessDayId・revisionを復元
```

この値は表示と入力初期値に使い、納品額入力画面では改めて全締め条件を計算しない。別端末/別タブで営業日が変わった場合も、保存時の `expected_business_day_id` / revisionをDBが検証するため、古い画面から別営業日へ保存されない。

browser stateが存在しない場合（URL直接入力、新規タブ、storage消去など）だけ、フォールバックとして `get_current_drink_delivery_editor` を1回呼ぶ。これは通常遷移の経路には含めない。

### 納品額保存

保存も1 mutationへまとめる。`save_drink_delivery_amount_v2` が現在営業日を解決し、営業日がなければ現行仕様どおり作成し、金額を検証・保存して応答を返す。現行のGET/Ensure/save/redirectの分割を行わない。

- 入力: `operation_id`、期待business day ID/revision、金額。
- 成功応答: 確定business day、`drink_delivery_amount`、`is_entered=true`、新revision、およびclosing dashboardのdrink delta。browser stateもこの応答で置換する。
- 競合/入力エラー: 保存せず、最新のeditor stateとエラーを返す。

同じ `operation_id` の再送では金額保存を二重処理しない。成功応答を入力画面へ適用し、締めトップへ移動する場合は移動先のdashboard同期が全パネルを確定する。

### 受入条件

- 締めトップの納品額パネル表示はclosing dashboard RPC以外のreadを送らない。
- 締めトップから納品額入力画面への通常遷移は、追加read RPC 0回である。browser stateがない復旧経路だけ、current + statusをまとめた1 RPCを許容する。
- 納品額保存は営業日作成の有無にかかわらず1 write RPCであり、成功後に納品額statusを再読取しない。

## 12. キャスト売上額調整

### 結論: 詳細snapshotの1 readは必要、ただし2段階読取と保存後再読取は不要

**必要である。** この画面は、会計済み伝票ごとの小計/総額、指名キャスト、指名開始時刻、既存調整額、配分候補を表示して入力する。締めトップのdashboardが持つ「未調整件数」だけでは画面を構成できない。

ここで必要なのは営業中一覧の汎用snapshotではなく、会計済み伝票に絞った **cast-sales-adjustment overview snapshot** である。現行 `get_business_day_cast_sales_adjustment_overview` は、status・対象伝票一覧・全伝票のdetailをすでに1応答で返している。この詳細read自体は残す。

一方、現行のページGETは `EnsureAsync` / store context / `get_current_business_day` の後にoverviewを取得するため、current runtime cacheのmiss時にoverviewと別RPCになる。これを次の1 readへ縮める。

```text
Razor GET: master設定を使ったshellを0 RPCで表示
  → get_current_cast_sales_adjustment_overview 1回
      = 現在営業日の解決
      + amount basis / split mode（master cache miss時のfallbackを含む）
      + status + checked-out対象伝票 + 全detail + revision
```

`business_home_snapshot` を流用しない。調整画面に必要な会計済み伝票の確定金額・checkout情報・配分候補は別の読み取りモデルであり、営業中一覧のpayloadへ混ぜるとpoll payloadも契約も不必要に大きくなる。

### 保存・確認

現行の単票保存は `LoadAsync` → `save_cast_sales_adjustment` → `LoadAsync`、全件確認も `LoadAsync` → batch saveという流れである。保存RPCの応答が件数だけのため、画面を再構成するには全overviewを読み直している。

後継mutationは、単票/全件とも成功応答に以下を含める。

- `operation_id`、business day ID/revision、保存したadjustment行。
- 単票保存: 更新後の対象伝票detailとoverview status、closing dashboardのcast-sales-adjustment delta。
- 全件確認: 更新後status（通常 `missing_slip_count=0`）とclosing dashboard delta。
- 競合/会計取消など: 保存せず、最新overviewまたは影響伝票detailと理由を返す。

ブラウザは応答のdetail/deltaを適用するため、成功直後にoverview readを追加しない。再送で二重に調整を確定しないよう `operation_id` を持たせ、対象伝票/checkoutのversionも検証する。

### 受入条件

- 初期画面の動的読取は、current business dayとoverviewを別々に取得せず1 RPCである。
- 会計済み伝票の詳細snapshotを取得する1 readは残し、締めトップの件数や営業中一覧snapshotで代用しない。
- 単票保存・全件確認の成功後にoverviewを再読取しない。
- 会計取消または別端末調整との競合時に、古い配分を上書きしない。

## 13. ドリンクバック調整（旧: シャンパンバック入力）

### 合意事項: 納品額と同じbrowser-state優先にする

締めトップのdashboard応答に、ドリンクバック調整用の小さいeditor fragmentを含める。通常の「締めトップ → ドリンクバック調整」遷移は、納品額と同様にこのbrowser stateを使い、追加read RPCを送らない。

```text
get_current_closing_dashboard
  → drink_back_editor fragment をbrowser stateへ保存
  → ドリンクバック調整画面のRazor GETは0 RPCでshellを返す
  → browserがeditor fragment + cast master cacheから行を構成
```

editor fragmentには、営業日ID/revision、出勤キャストの必須行、既に保存された出勤外調整行、必須行の完了状態、合計額を含める。チェックボックスで出勤外キャストを表示するための全active cast名簿はmaster cacheから得る。dashboardの30秒pollごとに名簿全体を返さない。

URL直接入力、新規タブ、browser state消去などの復旧経路だけ、`get_current_drink_back_editor` を1回呼ぶ。これは現在営業日解決とeditor fragmentを同じ応答に含める。

### 画面と入力契約

- 画面名、ナビ、文言、API上の論理名を **ドリンクバック調整** / `drink_back_adjustment` へ変更する。既存の「シャンパンバック」は旧称としてのみ扱う。
- 通常表示は当日出勤キャストの必須行を示す。各金額は**符号付きの整数円**とし、マイナス値を許容する。
- 「出勤していないキャストを表示」チェックボックスを置く。ONのとき、activeなキャストmasterのうち当日出勤していないキャストを任意調整候補として表示する。
- 出勤外キャストは、利用者が明示的に調整対象として選択・入力した行だけ送信する。既存の出勤外調整を削除するための明示的なremove操作も送信できるようにする。全非出勤キャストの0円行を一律保存しない。
- すでに保存済みの出勤外調整行は、非表示状態でも存在を分かるよう件数/badgeを表示し、ONにすると編集・削除できるようにする。

### 保存と締め条件

`save_drink_back_adjustments_v2` は1 transaction・1 RPCで保存する。

- 入力: `operation_id`、期待business day ID/revision、出勤者全員の必須行、明示的な出勤外調整行、削除する出勤外キャストID。
- DBは営業日がopenであること、出勤者集合、キャストの店舗所属・有効状態、重複、符号付き整数範囲を検証する。出勤外行かどうかはクライアント申告を信用せず、DBが当日勤怠から導く。
- 成功応答: 更新後のeditor fragment、ドリンクバック調整status/合計、closing dashboard delta、新revision。ブラウザstateをこれで置換し、追加readしない。
- 再送: 同じ `operation_id` は二重計上せず、同じ確定応答を返す。

締め条件の必須対象は**出勤キャストだけ**である。出勤外の任意調整行はrequired/missing countを増やさず、未入力を理由に締めを止めない。一方、合計のドリンクバック金額には出勤/出勤外を問わず、符号付きで反映する。

### read model・切替方針

現行 `champagne_back_*` read modelは、出勤者だけ・0以上・提出集合が出勤者集合と完全一致、という制約を持つため後継に置き換える。新readinessのフィールドは `drink_back_required_cast_count`、`drink_back_completed_cast_count`、`drink_back_missing_cast_count`、`drink_back_total_amount` とする。

後方互換は維持しない。`champagne_back_*` のRPC・DTO・画面文言・物理モデルは、新しい `drink_back_*` 契約へまとめて置換する。旧RPCへのfallback、旧新の二重read/write、旧payloadの変換層は作らない。

既存の `store_business_day_champagne_backs` 行は、金額・キャスト・営業日を対応する新物理モデルへ一方向に移す。移行済みの過去営業日を再計算せず、旧テーブル/RPC/DTOは切替リリース後に廃止する。これは履歴保全のためのデータ移行であり、旧画面・旧APIを動かし続ける要件ではない。

### 受入条件

- 締めトップからの通常遷移では、ドリンクバック調整画面の追加read RPCは0回である。
- 出勤キャストの必須行は0円・正額・負額を保存できる。
- 出勤外キャストの任意調整行を追加・更新・削除でき、未選択の非出勤キャストは保存されない。
- 出勤外調整の有無は締め条件のrequired/missing countを変えないが、符号付き合計額には反映される。
- 保存成功後にoverview/readinessを再読取しない。

## 14. 領収書簡易入力

### 問題の整理

現行の1件処理は、Razor GETで現在営業日を強制取得し、キャスト前渡金候補のため当日勤怠を取得し、未処理証憑一覧を取得する。保存時も同じ事前読取を行い、さらに会社IDのcache missでは `get_context` を呼んでから `quick_enter_receipt` を実行する。成功後は未処理一覧を再読してredirect GETを行う。したがって、一覧cacheの当たり外れにより1操作あたりのRPC数が大きく揺れ、次の証憑を出すだけのために画面全体を再読・再描画している。

ここで必要なのは営業中の全snapshotではない。領収書入力に必要な動的状態は、**現在の作業対象、残件数、キャスト前渡金を選んだ場合だけ使う当日出勤キャスト、作業キューのrevision** である。科目一覧などの静的UI定義はshellに含めるかmaster cacheに置く。Google Driveのプレビュー本体はDB RPCに混ぜず、現在の対象が持つ `drive_file_id` を使い、iframe/previewキャッシュとして遅延表示する。

### 読取: 領収書ワークキューを1 RPCにする

Razor GETは状態非依存のshellを返し、browserが `get_current_receipt_work_queue` を1回呼ぶ。このRPCはDB内で部署の会社・現在営業日を解決してから、未処理証憑を安定順で選び、次を返す。候補bufferの続き取得では、browserが直近候補の位置を表す不透明な `resume_cursor` を渡す。

- `queue_revision`、`pending_count`、現在の `work_item` と少数の次候補buffer（document ID、ファイル名/Drive file ID、初期日付・金額、表示に必要な最小メタデータ）。証憑画像そのものは含めない。
- 各候補の `work_item_token`（document IDと、当該証憑が未処理であることを表すpending version）。短いTTLでは失効させず、対象が処理・除外・更新されるまで同じ値を保つ。
- 次bufferの取得位置を表す `resume_cursor`、現在営業日ID/日付。
- キャスト前渡金に必要な当日出勤キャストだけ。営業日なしなら空集合を正規応答とする。
- 空キューなら `work_item=null`。この状態をエラーや締め不可として扱わない。

一覧全体をクライアントに持つ必要はない。現行の「スキップ」はDB状態を変更しないため、browserは次候補buffer内を進めるだけでよい。bufferを使い切ったときは最後に表示した候補の `resume_cursor` で同じreadを補充し、同じ証憑を先頭へ戻さない。直接URL、新規タブ、queue revision不一致、通信失敗からの復帰ではcursorを捨てて先頭から読み直す。通常の保存直後には呼ばない。

### 進行mutation: outboxを1件ずつ直列同期する

`advance_receipt_work_queue_v2` を唯一の永続進行mutationとする。browser outboxの先頭commandを**常に1件だけ**送る。`action` は `save`、`exclude_scan_mistake` とし、commandは `operation_id` と対象固有の `work_item_token` を持つ。応答を受けてから次のcommandを送る。`skip` は永続操作ではないため、このmutationを呼ばない。

- `save` は入力、対象証憑の未処理性、会社/部署、科目、整数金額、キャスト前渡金なら現在openの営業日・当日出勤キャストをDB内で検証する。仕訳payloadの組立、仕訳・証憑リンク・前渡金行の保存まで同一transactionに含める。会社IDを事前に `get_context` で読むことはしない。
- `exclude_scan_mistake` は対象証憑を除外する。現在のように削除扱いにするか専用の除外statusにするかは業務決定だが、いずれにしても次件選定と同じtransactionで行う。
- 将来、保留/担当者割当のように永続状態を変える「スキップ」を導入する場合だけ、別actionとしてこのmutationに追加する。単なる画面上のスキップをDBへ記録しない。
- 応答は `confirmed` / `stale_work_item` / `validation_error`、処理済みdocument ID、更新後 `queue_revision`、`pending_count`、次の `work_item` と候補buffer、次件に必要な当日出勤キャスト、必要なら締めdashboardの `pending_receipt_count` deltaを含める。各証憑の仕訳・証憑リンク・前渡金行は1 transactionで確定させる。browserは未確定のローカル操作を残して応答をrebaseし、redirect GETも `get_pending_receipts` 再読も行わない。

`operation_id` は保存と除外で必須とし、タイムアウト再送時にも仕訳・前渡金を二重作成しない。operation結果はoutbox再送期間以上サーバーに保持する。全キュー共通のrevisionを保存可否の条件にしてはいけない。先行する証憑Aの確定でrevisionが変わっても、すでに画面に出した独立の証憑Bをパイプライン処理できるよう、サーバーはB自身のpending version token・未処理性だけを検証する。別端末が先に同じ証憑を処理した、対象が既に除外された、営業日/勤怠が変わった場合はそのcommandだけ保存せず、`stale_work_item` と最新のqueue revisionを返す。

### 即時に次票を表示するbrowser outbox

利用者が保存または除外を操作した瞬間に、browserは入力と `operation_id` を永続local outbox（IndexedDB等）へ書き、現在票を「同期中」としてから候補bufferの次票を即時表示する。通信応答は待たない。outboxは先頭commandを直ちに送るが、通信中にさらに積まれたcommandは保持し、先頭の応答を受けてから次の1件を送る。

- 通常の入力体験は `保存 → 次票表示` がローカル操作だけで完了する。ヘッダに「同期中 n件」を常時示し、confirmed応答を受けたときだけ確定表示に変える。
- 回線断・タブ再読み込み・一時的な5xxでもoutboxと入力値は残る。再開時は同じ `operation_id` で再送するため、二重仕訳にならない。
- `validation_error` または `stale_work_item` のcommandは自動破棄せず、失敗一覧へ移す。当該commandの成否は確定しているため、後続commandの直列送信は続ける。失敗票は入力値を再表示し、修正後に新しい `operation_id` で再投入または明示的に取り下げる。
- タイムアウト・接続断・5xxなど成否が不明な通信失敗だけは、同じ `operation_id` の再送結果が分かるまで先頭commandとして保持し、後続を送らない。これにより、保存済みか不明な仕訳を飛ばしたまま同期順を進めない。
- 未同期commandがある間は「全件同期済み」と表示しない。締めトップへ戻る操作は可能にするが、同期中件数と失敗件数を表示して、未確定データを確定済み件数へ混ぜない。

### 画面遷移と状態の扱い

```text
締めdashboard（pending_countのみ）
  → 領収書shell GET: 0 RPC
  → get_current_receipt_work_queue: 1 RPC
  → skip: 候補buffer内で画面を切替（0 RPC）
  → save / exclude: local outboxへ追加し、次票を即時表示（0 RPC待機）
  → advance_receipt_work_queue_v2(command): 背景で1 RPC、応答後に次の1件を送信
  → 応答をlocal stateへrebase（追加read・redirectなし）
```

領収書件数は締めdashboardの情報表示であり、`can_close` / `block_reasons` の必須条件には入れない。進行mutationのdeltaをdashboard用browser stateにも反映し、締めトップへ戻った直後に件数だけを取り直さない。Drive認証が必要な場合だけ、プレビューアクセスの認証導線は別に保つ。これは外部認証の遷移であり、作業キューのDB RPCを追加する理由にはしない。

### 受入条件

- 領収書画面の初期動的取得は、現在営業日・出勤キャスト・未処理証憑を別々に呼ばず1 RPCである。
- 保存とスキャンミス除外はoutboxの各commandを1 RPCで直列同期し、成功後に未処理一覧read・redirect GET・会社ID取得を発生させない。通常のスキップは候補buffer内で0 RPCである。
- 保存または除外を操作すると、通信応答を待たずに次票を表示できる。同期中・失敗・確定済みを区別し、失敗入力を失わない。
- キャスト前渡金の営業日・出勤者検証と仕訳保存はサーバーtransaction内で完結し、クライアントに渡した候補を信頼しない。
- 同じ `operation_id` の再送では二重仕訳・二重前渡金を作らない。
- 空キューと別端末競合は正常な状態遷移として表示でき、古い証憑へ保存しない。

## 15. 管理マスタ（卓・キャスト・スタッフ・商品・指名・料金）のGET

### 結論: 管理画面ごとのGETを作らず、管理マスタsnapshotを共有する

卓、キャスト、スタッフ、商品/カテゴリ、指名バック、料金プランは、営業日や伝票のようなruntime stateではなく**部署単位の管理マスタ**である。個別画面ごとに読ませず、browserが保持する `management_master_snapshot` を共有する。

| 状態 | Razor GET中のRPC | browserの初期同期 | 合計 | 表示 |
|---|---:|---:|---:|---|
| 管理master cache warm | 0 | 0 | 0 | cacheから即時描画 |
| 初回・cache消去・明示更新 | 0 | `get_management_master_snapshot` 1回 | 1 | shell後に対象領域を同期表示 |
| 自画面の保存成功後 | 0 | 0 | 0 | mutation応答のdeltaを反映 |

`get_management_master_snapshot` は、卓管理一覧、キャスト管理一覧（ドリンクメモを含む）、スタッフ管理一覧、商品カテゴリと商品、指名バック設定、料金プランと、全体/領域別の `master_revision` をまとめて返す。商品数が通常の店舗規模で収まることを前提に、管理画面を順に開くたびに往復するより1回の初期取得を優先する。証憑画像や営業中snapshotのような重いruntime payloadは含めない。

営業中トップ用の `get_business_home_bootstrap_v2` には管理専用の詳細を含めないため、管理画面を初めて開くときのこの1 RPCは必要である。ただし、卓→キャスト→スタッフ→商品→指名→料金の通常遷移で再度取得しない。

### 現行から除くもの

現行の各管理repositoryはcache miss時に `get_store_bootstrap` を呼び、同一payloadから管理用一覧を水和している。この集約方向は正しいが、管理画面専用データまで営業中top bootstrapへ混ぜている。後継では管理master snapshotへ分離する。

キャスト管理とスタッフ管理は、一覧取得と並列して `GetCurrentAsync` で現在営業日を読んでいる。画面の一覧描画には必要なく、キャストのドリンクメモ保存後のruntime cache無効化にだけ使われている。このため管理GETから現在営業日読取を外す。ドリンクメモmutationはサーバー側で関連runtime cacheを無効化するか、後続の営業中snapshotが再評価されることを応答で示せばよい。

また、現行の保存後はmaster cacheを消して `LoadAsync` で一覧全体を読み直す。これは、保存RPCの応答がID/件数だけで次の画面状態を返さないためである。

### cache・更新契約

- browserのactive cache keyは `management-master:{department_id}:v1` とする。payload内部に全体revisionと領域別revisionを持ち、卓だけの変更で商品catalogを破棄しない。revision付きの履歴keyは、明示的に複数版を保持したい場合だけ追加する。
- 管理画面の通常遷移・10秒pollは行わない。他端末の変更を拾う契機は、明示更新、一定時間以上離れた後のfocus、または将来のpushとする。そのときだけ `known_revision` を渡し、変更なしなら小さい応答にする。
- `save_*_master_v2` は `operation_id` と対象領域の期待revisionを受け、成功時には新revisionと必要十分な `master_delta` を返す。例えば卓/キャスト/スタッフはupsert・無効化行、商品はcategory/itemを含むcatalog delta、指名と料金は更新後の設定全体を返す。
- browserはdeltaで管理master cacheを更新する。同一端末で開いている営業中/注文画面には、対象の運用マスタも同時に更新または無効化したことを通知する。保存成功直後に管理GETを再実行しない。
- 別端末が同じ領域を編集した競合は、古いrevisionで上書きせず、最新の当該領域deltaと競合理由を返す。画面は未送信入力を残して再編集できる。

料金プラン更新は現在営業日の売上見積りに影響しうるが、管理GETで営業中snapshotを読む理由にはしない。料金保存mutationの結果で管理cacheを更新し、営業中画面は次の通常snapshot同期で再計算結果を受け取る。即時反映が業務上必要なら、料金mutationの応答に限ってbusiness-home deltaを追加する。

### 受入条件

- 管理master cache warm時、6管理画面への遷移はSupabase RPC 0回である。
- cold時も、各画面が個別に卓・キャスト・スタッフ・商品・指名・料金を取得せず、管理master snapshot 1 RPCで表示できる。
- キャスト管理・スタッフ管理GETは、管理一覧のためだけにcurrent business dayを読まない。
- 管理保存後は、一覧全体の再読取をせず、mutation応答のmaster deltaで対象画面と共有cacheを更新する。
- 他端末更新との競合で、古いマスタ設定を無言で上書きしない。

## 16. 実装順と完了条件

この計画は、ページごとにRazorの読取/POSTを置換していく大きな変更である。先に共通基盤を作らず各ページから個別にv2を作ると、cache・error・再送の挙動が再び分岐する。次の順で実装する。

1. **同期基盤**
   - `SyncStore`、shell hydrate、JSON read/mutation client、single-flight、revisionを比較して古い応答を捨てる仕組みを作る。
   - `operation_id` のサーバー保存、部署/対象別の認可、共通の `confirmed` / `conflict` / `validation_error` / `unavailable` 応答形式を定義する。
   - network traceで「成功mutationの直後に同じ画面のreadを送らない」ことを検証できる計測を入れる。
2. **静的masterと管理画面**
   - `get_management_master_snapshot` と管理master deltaを実装し、卓・キャスト・スタッフ・商品・指名・料金を同じ`SyncStore`へ移す。
   - 管理画面の現在営業日読取と保存後の全体再読取を除去する。
3. **営業中のreadと一般編集**
   - `get_current_business_home_snapshot`、`sync_business_home_changes_v2`、伝票作成を実装し、トップのshell/自動同期/変更queueを切替える。
   - 会計発行・確定・取消は一般編集queueとは別に、各原子的mutationとして移す。
4. **注文・勤怠・締めのread/mutation**
   - 注文候補、注文登録、勤怠、closing dashboard、納品額、キャスト売上調整、ドリンクバック調整を、それぞれ本書の画面用read modelとmutationへ移す。
5. **領収書を最後に移行する**
   - `resume_cursor`付き領収書read、document pending version、`advance_receipt_work_queue_v2`、IndexedDB outboxを実装する。
   - outboxは常に1件だけ送信し、既知の業務エラーは失敗一覧へ、不明な通信失敗は同一`operation_id`の確認まで再送待機にする。
6. **切替と廃止**
   - 各画面のv2 UI・SQL・受入テストが揃ったリリースで切替える。切替済み画面から旧handler/RPCを外し、全画面の切替完了後に旧DTO/RPC/物理モデルを削除する。
   - ドリンクバックの既存行は一方向移行を確認してから旧物理モデルを廃止する。

各段階の完了条件は、対象画面のnetwork trace、別端末競合、同じ`operation_id`の再送、営業日切替、cache cold/warmの組合せを自動テストまたは再現手順で確認できることである。DB migrationを伴う段階は、SQLの適用順・戻せないデータ変更・確認RPCをHANDOFFへ残す。

## 次に確認する画面

日報の読取・保存を確認する。
