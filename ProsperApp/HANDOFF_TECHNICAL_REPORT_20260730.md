# ProsperApp 技術調査レポート

作成日: 2026-07-30

## 調査範囲と前提

- `Docs/` と `CONTEXT.md` は削除対象として扱い、`HANDOFF.md` は残した。
- 本レポートは設計、コード境界、責務分離、冗長性、RPC/SQLとの契約、待ち時間に関わるI/O配置を中心に静的調査した。
- ローカルサーバーはAGENTS.mdの運用に従い起動していない。SQL実行とAzureデプロイは許可されていたが、今回の成果物は調査レポートであり、本番状態変更を必要としないため実行していない。
- 用語は `codebase-design` の語彙に合わせ、Interface、Implementation、Seam、Adapter、Moduleとして整理した。

## 総評

ProsperAppは、外部RPCを `ISupabaseRpcClient` に閉じ込めるAdapter、営業中画面のlocalStorageキュー、会計前の未送信操作flush、締め作業の条件集約など、現場運用の事故を減らす骨格はかなり作られている。

一方で、権限、締め条件、営業中編集、時刻処理、エラー表現のSeamが浅い。特に管理者モードがクライアントCookie由来でサーバー側の強い権限判定になっていない点、C#表示判定とSQL実行判定が二重化している点、RPC失敗を空配列や0へ畳む点は、実運用では「できてはいけない操作ができる」「失敗がデータなしに見える」「画面とDB結果がずれる」形で表面化しやすい。

## よい設計

### Supabase RPCのAdapterは薄くまとまっている

`ISupabaseRpcClient` は `HasAccess`、配列結果、スカラー結果にInterfaceを絞っている。`SupabaseRpcClient` 側でURL、キー、関数名、ヘッダー、レスポンス正規化を吸収しており、外部RPCへのAdapterとして読みやすい。

根拠:

- `Infrastructure/Supabase/ISupabaseRpcClient.cs:10`
- `Infrastructure/Supabase/SupabaseRpcClient.cs:74`
- `Infrastructure/Supabase/SupabaseRpcClient.cs:110`
- `Infrastructure/Supabase/SupabaseRpcClient.cs:162`

### 伝票作成の入力整形はPageModelから分離されている

`CreateSlipEditor` は初期値、入力整形、検証、エラー変換を持つ小さなModuleになっており、PageModelに直接書くよりテストしやすい。

根拠:

- `Features/Slips/CreateSlipEditor.cs:16`
- `Features/Slips/CreateSlipEditor.cs:41`
- `Features/Slips/CreateSlipEditor.cs:171`

### 営業中画面は非同期編集をまとめてflushする設計になっている

営業中編集はlocalStorageに未送信操作を保持し、画面遷移や会計開始前にflush完了を待つ。通信断や待ち時間に対して実務上の耐性がある。

根拠:

- `wwwroot/js/features/business-home.js:94`
- `wwwroot/js/features/business-home.js:1605`
- `wwwroot/js/features/business-home.js:1745`
- `wwwroot/js/features/business-checkout.js:341`
- `wwwroot/js/features/business-checkout.js:374`

### 締め作業は条件を画面で集約している

締めトップは未会計伝票、酒代、勤怠、キャスト売上額調整、領収書をまとめて確認し、条件が揃うまで最終締めを止める設計になっている。

根拠:

- `Pages/Closing/Index.cshtml:34`
- `Pages/Closing/Index.cshtml.cs:282`
- `Pages/Closing/Index.cshtml:210`

## 重要な技術課題

### P0: 管理者モードがクライアントCookie由来で、サーバー権限のSeamとして弱い

管理者モードは `LocalSettings.IsAdminMode` に含まれ、`LocalSettingsProvider` がCookieから読み取る。設定画面の `CanAccessSettings()` は保存トークンまたはこのCookie由来の管理者モードで通る。さらに締め条件の無視も `IsAdminMode` を使っている。

これは「画面表示の好み」や「端末ローカル設定」と、「本当に許可された管理操作」が同じ設定に載っている状態で、Seamが浅い。サーバー側では認証済みユーザー、店舗、ロール、セッション署名などの強いInterfaceに分離すべき。

根拠:

- `Features/Settings/LocalSettings.cs:23`
- `Services/LocalSettingsProvider.cs:33`
- `Pages/Settings/Index.cshtml.cs:227`
- `Pages/Settings/Index.cshtml.cs:255`
- `Pages/Closing/Index.cshtml.cs:260`
- `Pages/Closing/Index.cshtml.cs:270`

推奨:

- `LocalSettings` から管理者権限を外し、端末表示設定だけにする。
- 管理者権限はサーバー側セッションまたはDB/RPCで判定する。
- 締め条件無視、デバッグ削除、マスタ変更のような破壊的操作は同じ権限判定Serviceに集約する。

### P1: マスタ変更の一部POSTに管理者モード確認がない

商品マスタではカテゴリ保存のみ `IsAdminMode` を確認しているが、商品追加、削除、並べ替えのPOSTでは同じ確認が見当たらない。UIが管理者向けに隠していても、POSTできるならサーバー側権限としては不足する。

根拠:

- `Pages/Management/Items.cshtml.cs:54`
- `Pages/Management/Items.cshtml.cs:82`
- `Pages/Management/Items.cshtml.cs:114`
- `Pages/Management/Items.cshtml.cs:144`

推奨:

- `OnPostSaveItemAsync`、`OnPostDeleteItemAsync`、`OnPostReorderItemsAsync` でも共通の管理者確認を通す。
- キャスト管理、設定、デバッグ削除も同じ権限Serviceを使い、ページごとの個別判定を減らす。

### P1: 締め条件の業務ロジックがC#表示判定とSQL実行判定に二重化している

画面側は `BusinessDayClosingReadiness.CanClose` で締め可能か判断し、実行時は `store.close_business_day` 側で条件を再判定する。この二重化自体は防御として必要だが、条件の定義が別々に維持されると、画面上の可否とDBの成否がずれる。

根拠:

- `Features/BusinessDays/BusinessDayModels.cs:103`
- `Pages/Closing/Index.cshtml.cs:240`
- `Pages/Closing/Index.cshtml.cs:260`
- `Sql/store_rpc/01_business_day.sql:690`

推奨:

- DB側に「締め準備状態を返すRPC」を寄せ、C#はその結果を表示するだけにする。
- 画面用の理由メッセージもRPCの構造化結果から組み立てる。
- C#の `CanClose` は最終判断ではなく表示補助として扱う。

### P1: 営業中編集の契約がJS、PageModel、Repository、SQLに分散している

営業中編集では操作種別、payload名、検証、エラー翻訳が複数層に重複している。深いModuleは `store.flush_business_home_changes` 側にあるが、そのInterfaceが型として表現されず、JSの文字列、C#の `JsonElement`、SQLのJSON処理で暗黙に接続されている。

根拠:

- `wwwroot/js/features/business-home.js:69`
- `wwwroot/js/features/business-slip-editor.js:815`
- `Pages/Index.cshtml.cs:203`
- `Features/Slips/StoreSlipModels.cs:187`
- `Infrastructure/Supabase/SupabaseStoreSlipRepository.cs:309`
- `Sql/store_rpc/09_business_home_snapshot.sql:448`

推奨:

- C#側に操作種別ごとのDTOと検証Serviceを置き、`JsonElement` をPageModelから追い出す。
- SQL/RPCのoperation名をC#定数または生成物として一元化し、JSはサーバーから渡された契約を使う。
- エラーコードからUI文言への変換を1箇所に寄せる。

### P1: PageModelが入力正規化、業務検証、RPC orchestration、表示JSON作成まで抱えている

`IndexModel`、`AttendanceModel`、締め系PageModelは、フォーム入力の正規化、業務検証、Repository呼び出し、画面再構築、JSONレスポンス作成をまとめて担っている。PageModelが浅く広いModuleになり、C#単体テストのsurfaceもPageModelに寄る。

根拠:

- `Pages/Index.cshtml.cs:11`
- `Pages/Index.cshtml.cs:196`
- `Pages/Index.cshtml.cs:404`
- `Pages/Attendance.cshtml.cs:60`
- `Pages/Attendance.cshtml.cs:388`
- `Pages/Closing/Index.cshtml.cs:64`
- `Pages/Closing/Index.cshtml.cs:282`

推奨:

- 営業中画面: `BusinessHomeApplicationService` のようなModuleを作り、snapshot取得、flush、会計開始前同期をまとめる。
- 勤怠: 入力行の正規化と業務検証を `AttendanceEditor` に寄せる。
- PageModelはHTTP入力、Service呼び出し、ViewModel設定に限定する。

### P2: RPC失敗が空配列、0、nullに畳まれ、UIが「データなし」と誤認しやすい

`SupabaseRepositoryBase.PostRpcArrayAsync` はアクセス不能やRPC失敗時に空配列を返す。Repositoryによっては0や既定値に畳む。結果として、通信失敗、権限不足、SQLエラーがUIでは「対象なし」に見える。

根拠:

- `Infrastructure/Supabase/SupabaseRepositoryBase.cs:19`
- `Infrastructure/Supabase/SupabaseRepositoryBase.cs:23`
- `Infrastructure/Supabase/SupabaseBusinessDayRepository.cs:255`
- `Infrastructure/Supabase/SupabaseBusinessDayRepository.cs:275`
- `Infrastructure/Supabase/SupabaseCastSalesAdjustmentRepository.cs:12`
- `wwwroot/js/features/create-slip-modal.js:64`

推奨:

- 読み取り系Repositoryの戻り値を `Result<T>` 型に寄せ、成功空配列と失敗を分離する。
- UIでは「データなし」「取得失敗」「権限/設定不足」を別表示にする。
- 障害時は再試行ボタンと最終更新時刻を出す。

### P2: キャッシュが期限なしで明示invalidateに強く依存している

マスタや営業日系キャッシュは `NeverRemove` かつ期限なしが中心。待ち時間削減には効くが、別プロセス、SQL直更新、Edge側修正、複数App Serviceインスタンスでは鮮度保証が弱い。

根拠:

- `Infrastructure/Supabase/StoreMasterCacheKeys.cs:27`
- `Infrastructure/Supabase/StoreMasterCacheKeys.cs:36`
- `Infrastructure/Supabase/SupabaseBusinessDayRepository.cs:60`
- `Infrastructure/Supabase/SupabaseStoreOrderRepository.cs:96`

推奨:

- 期限付きキャッシュに変更し、マスタは短めのTTLと明示invalidateの併用にする。
- 営業日や締め状態はruntimeキャッシュを短くし、重要操作後は必ず該当キーを消す。
- 管理画面でキャッシュクリアや最終取得時刻を確認できるようにする。

### P2: 時刻処理のSeamが複数に分かれている

`IStoreClock` はよい入口だが、静的 `StoreBusinessTime` や各PageModelの個別TimeZone処理も残っている。営業日跨ぎ、24時以降表示、日本時間、テスト時刻の扱いが分散する。

根拠:

- `Features/Shared/StoreClock.cs:3`
- `Services/StoreBusinessTime.cs:3`
- `Pages/Orders/Index.cshtml.cs:80`
- `Pages/Closing/Receipts.cshtml.cs:300`

推奨:

- 業務時刻は `IStoreClock` に集約し、静的ヘルパーから置き換える。
- 入力値、表示値、保存値の変換を明示的なメソッド名で分ける。
- 営業日跨ぎのテストケースをC#で追加する。

### P2: I/Oは並列化されているが、N+1や多重取得が残っている

一部の画面では `Task.WhenAll` で待ち時間を抑えているが、キャスト売上額調整や領収書プレビューなど、画面単位で複数RPC/Drive呼び出しに分散している処理が残る。

根拠:

- `Pages/Index.cshtml.cs:404`
- `Pages/Closing/Index.cshtml.cs:489`
- `Pages/Closing/CastSalesAdjustment.cshtml.cs:199`
- `Pages/Closing/CastSalesAdjustment.cshtml.cs:124`
- `Infrastructure/GoogleDrive/GoogleDriveFileService.cs:21`
- `Infrastructure/GoogleDrive/GoogleDriveFileService.cs:43`

推奨:

- キャスト売上額調整は一覧と詳細をまとめたRPCにする。
- 保存も1伝票ずつではなく、画面入力全体の一括保存を検討する。
- Driveプレビューはキャッシュヒット時にDB pending判定を省略できるか検討する。

### P2: 支払い手段がJSに固定されている

会計支払い手段は `cash`、`cat`、`paypay` がJSに固定されている。店舗の支払い手段が増減する場合、マスタ変更ではなくコード変更が必要になる。

根拠:

- `wwwroot/js/features/business-checkout.js:223`

推奨:

- 支払い手段をDBまたは設定から取得し、画面にはサーバーから渡す。
- SQL/RPCの支払い手段バリデーションも同じマスタを参照する。

### P3: Namespaceがフォルダ構造ほどModule境界を表していない

フォルダは `Features` と `Infrastructure` に分かれているが、namespaceは `ProsperApp.Services` や `ProsperApp.Models` に平坦化されている箇所が多い。Module境界が言語上に現れず、Interface/Implementation/Adapterの所在が読み取りにくい。

根拠:

- `Features/Slips/IStoreSlipRepository.cs:3`
- `Infrastructure/Supabase/SupabaseStoreSlipRepository.cs:7`
- `Infrastructure/GoogleDrive/GoogleDriveFileService.cs:5`

推奨:

- 既存コードを一気に改名せず、新規/変更対象からFeature単位のnamespaceへ寄せる。
- `ProsperApp.Features.Slips`、`ProsperApp.Infrastructure.Supabase` など、InterfaceとAdapterの位置を名前で読めるようにする。

### P3: C#業務Moduleの単体テストが薄い

JS契約テストとSQL契約テストはあるが、C# PageModel、Repository、業務ServiceをInterface越しに検証するテストプロジェクトは見当たらない。`CreateSlipEditor`、`OrderQueueService`、`StoreClock` はC#テストの候補になる。

根拠:

- `ProsperApp.csproj:1`
- `Tests/rpc-contract.test.mjs:26`
- `Tests/business-home-draft-contract.test.mjs:26`
- `Features/Slips/CreateSlipEditor.cs:16`
- `Features/Orders/OrderQueueService.cs:31`

推奨:

- `ProsperApp.Tests` を追加し、業務時刻、伝票作成、注文キュー、締め条件表示をテストする。
- RPC Adapterはfake `ISupabaseRpcClient` でRepository単体テストを追加する。

## 優先改善順

1. 管理者権限をCookie由来のローカル設定から分離する。
2. マスタ系POSTと締め条件無視を共通のサーバー権限Serviceに通す。
3. 締め条件の判定元をDB/RPCの構造化結果へ寄せる。
4. 営業中編集のoperation契約をDTO/定数/エラーコードで型付けする。
5. RPC失敗を空結果に畳むRepositoryから `Result<T>` へ段階移行する。
6. `IndexModel` と `AttendanceModel` から業務検証Serviceを切り出す。
7. C#テストプロジェクトを追加し、上記のSeamから検証を始める。
