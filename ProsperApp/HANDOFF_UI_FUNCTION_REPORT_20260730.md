# ProsperApp UI・機能・実務導線調査レポート

作成日: 2026-07-30

## 調査範囲と前提

- 画面の見やすさ、導線、保存/削除/会計後の遷移、待ち時間、二重送信、機能不足、実務上の迷いやすさを中心に静的調査した。
- ローカルサーバーはAGENTS.mdの運用に従い起動していないため、実ブラウザでの折り返し、重なり、実プリンタ連携、実通信時間は未確認。
- SQL実行とAzureデプロイは許可されていたが、本番状態変更を伴う確認は今回のレポート作成には不要なため実行していない。

## 主要フローと遷移

### 営業中トップ

- `/Index` は通常の営業中画面。
- 端末設定が注文端末モードの場合、GET時に `/Orders/Index` へリダイレクトする。
- 新規伝票作成は成功後も `/Index` に留まり、Pageを再描画する。
- 営業中の伝票編集はモーダル内で操作し、localStorageキューから非同期flushする。ページ遷移前と会計開始前に未送信操作の完了を待つ。

根拠:

- `Pages/Index.cshtml.cs:122`
- `Pages/Index.cshtml.cs:244`
- `wwwroot/js/features/business-home.js:1745`
- `wwwroot/js/features/business-home.js:1916`
- `wwwroot/js/features/business-checkout.js:341`

### 会計

- 営業中トップから会計伝票を発行し、支払いモーダルで確定する。
- 会計確定後はページ遷移せず、モーダルを閉じ、営業中一覧を再取得する。
- 領収書印刷は確定後に実行される。

根拠:

- `wwwroot/js/features/business-checkout.js:374`
- `wwwroot/js/features/business-checkout.js:467`
- `wwwroot/js/features/business-checkout.js:486`

### 注文端末

- `/Orders/Index` は通常ヘッダーを非表示にし、注文端末向けの画面になる。
- 注文送信後は同じページに留まり、キューをクリアする。
- 伝票一覧はJSで取得し、localStorageに未送信キューを保持する。

根拠:

- `Pages/Orders/Index.cshtml.cs:47`
- `Pages/Orders/Index.cshtml.cs:90`
- `Pages/Orders/Index.cshtml:186`
- `wwwroot/js/features/order-entry.js:64`
- `wwwroot/js/features/order-entry.js:313`

### 締め作業

- `/Closing/Index` は未会計伝票、酒代、勤怠、キャスト売上額調整、領収書を集約して表示する。
- 最終締め成功後は `/Index` にリダイレクトする。
- `/Closing/Attendance` から勤怠保存した場合は `/Closing/Index` に戻る。通常の `/Attendance` から保存した場合は同じ画面に戻る。
- 酒代保存後は `/Closing/Index` に戻る。
- 領収書は保存、スキップ、スキャンミス登録後、次の対象があれば次へ、なければ `/Closing/Index` に戻る。

根拠:

- `Pages/Closing/Index.cshtml:34`
- `Pages/Closing/Index.cshtml.cs:240`
- `Pages/Attendance.cshtml.cs:160`
- `Pages/Closing/DrinkCost.cshtml.cs:89`
- `Pages/Closing/Receipts.cshtml.cs:162`

### 設定・マスタ

- 設定保存後は `/Index` に戻る。
- 商品マスタのカテゴリ保存、商品追加、削除、並べ替えはPOST後に商品マスタ画面へ戻る。
- キャスト管理は作成、削除、ドリンクメモ更新後にキャスト管理画面へ戻る。

根拠:

- `Pages/Settings/Index.cshtml.cs:128`
- `Pages/Management/Items.cshtml.cs:79`
- `Pages/Management/Items.cshtml.cs:111`
- `Pages/Management/Items.cshtml.cs:141`
- `Pages/Management/Items.cshtml.cs:172`
- `Pages/Management/Casts.cshtml.cs:86`
- `Pages/Management/Casts.cshtml.cs:104`
- `Pages/Management/Casts.cshtml.cs:138`

## よい実務設計

### 通常フォーム送信と画面遷移のローディング基盤がある

共通のローディングオーバーレイ、二重送信防止、戻る/復元時の解除が入っている。現場端末での連打や待ち時間中の誤操作を減らす土台として有効。

根拠:

- `Pages/Shared/_Layout.cshtml:104`
- `wwwroot/js/site.js:4`
- `wwwroot/js/site.js:31`
- `wwwroot/js/site.js:320`
- `wwwroot/js/site.js:344`

### 営業中画面は通信断と待ち時間に配慮されている

未送信操作をlocalStorageに保持し、flush中の状態を表示し、画面遷移前に保存完了を待つ。会計開始時も未送信操作を待ってから会計伝票発行へ進む。

根拠:

- `wwwroot/js/features/business-home.js:94`
- `wwwroot/js/features/business-home.js:1605`
- `wwwroot/js/features/business-home.js:1745`
- `wwwroot/js/features/business-home.js:1916`
- `wwwroot/js/features/business-checkout.js:341`
- `wwwroot/js/features/business-checkout.js:374`

### 注文端末は端末運用に向いたキューと再取得導線を持つ

注文端末は未送信キューを保存し、伝票一覧を自動更新し、消えた伝票のキュー除去、再取得ボタンを持つ。注文入力だけをする端末としては実務上の耐性がある。

根拠:

- `wwwroot/js/features/order-entry.js:64`
- `wwwroot/js/features/order-entry.js:287`
- `wwwroot/js/features/order-entry.js:313`

### 締めトップは作業全体の見通しがよい

締め作業に必要な確認項目が1画面にまとまっており、条件が揃うまで最終締めを止める。担当者が「何を終わらせれば締められるか」を把握しやすい。

根拠:

- `Pages/Closing/Index.cshtml:34`
- `Pages/Closing/Index.cshtml:210`

## 重要なUI・機能課題

### P1: 会計時刻の初期値が深夜帯で現在時刻に合わない可能性が高い

会計時刻のoption valueは `00:00` から `23:55` で、表示だけ `24:00` 以降に変換される。一方、JSの `setDefaultClosedTime()` は深夜帯で `24:xx` から `35:xx` のvalueを探すため、該当optionが存在しない。深夜営業中に会計時刻の初期選択が現在時刻へ更新されず、古い選択値や先頭値のままになる可能性がある。

根拠:

- `Features/Shared/StoreClock.cs:53`
- `Pages/Index.cshtml.cs:103`
- `Pages/Index.cshtml:339`
- `wwwroot/js/features/business-checkout.js:123`

推奨:

- option valueは保存用の `00:xx` のまま扱い、JSの初期値も `rounded.getHours()` の0-23値で設定する。
- 表示ラベルだけ `FormatBusinessTimeOption` と同じ24時以降表記にする。
- 11:55、12:00、23:55、00:00、02:30のケースをテストに追加する。

### P1: 会計確定後の領収書印刷失敗が握りつぶされる

会計確定後、領収書印刷は `catch(() => {})` で失敗を無視している。会計自体は確定し、モーダルも閉じるため、スタッフが印刷失敗に気づかない可能性がある。

根拠:

- `wwwroot/js/features/business-checkout.js:486`
- `Pages/Index.cshtml:24`

推奨:

- 初回印刷失敗時は画面上部または会計完了エリアに明確な警告を出す。
- 再印刷ボタンを即時表示し、失敗理由も残す。
- 印刷処理の結果を監査ログまたは画面状態に残す。

### P1: 商品マスタに既存商品の編集導線が見当たらない

商品マスタの `SaveItem` は `ItemId = null`、`IsActive = true` に固定され、既存商品の更新ではなく追加として扱っている。既存行は価格、種別、バック対象が表示中心で、料金改定や商品名修正は削除/再追加前提に見える。

根拠:

- `Pages/Management/Items.cshtml.cs:89`
- `Pages/Management/Items.cshtml:170`
- `Pages/Management/Items.cshtml:212`

推奨:

- 既存商品のインライン編集または編集モーダルを追加する。
- 価格改定履歴が必要なら、既存商品更新ではなく有効期間付きマスタとして設計する。
- 削除/再追加が会計履歴や過去伝票に与える影響を画面に明示する。

### P2: キャスト管理に名前・入店日の編集導線が見当たらない

キャスト管理は新規作成、削除、ドリンクメモ更新が中心で、既存キャストの名前や入店日の修正導線が見当たらない。誤登録や改名時に削除/再作成へ寄りやすい。

根拠:

- `Pages/Management/Casts.cshtml:61`
- `Pages/Management/Casts.cshtml.cs:106`

推奨:

- キャスト基本情報の編集導線を追加する。
- 過去伝票との関係を崩さないよう、削除ではなく非表示/退店の状態管理を明確にする。

### P2: 注文端末モードから通常営業画面へ戻る導線が弱い

注文端末モードでは `/Index` が `/Orders/Index` に転送され、通常ヘッダー/ナビも非表示になる。端末専用なら妥当だが、設定ミスや一時的な切替時に、営業中画面へ戻る導線が管理者設定中心になる。

根拠:

- `Pages/Index.cshtml.cs:122`
- `Pages/Shared/_Layout.cshtml:44`
- `Pages/Orders/Index.cshtml:15`

推奨:

- 注文端末画面に現在モードと設定変更導線を明確に出す。
- 管理者解除後の戻り先を `/Index` にする。
- 店舗スタッフが使う端末なら、誤操作防止のため「営業中画面へ戻る」は管理者確認付きにする。

### P2: 営業中画面の手動更新操作が抑止される

`F5` / `Ctrl+R` が営業中画面で抑止される。未保存変更がある場合の保護としては有効だが、未保存変更がないときも手動更新できないように見える。

根拠:

- `wwwroot/js/features/business-home.js:1970`

推奨:

- 未送信操作がある場合だけ更新を止める。
- 未送信操作がない場合は通常更新を許可する。
- 画面内に明示的な再取得ボタンを置き、最終更新時刻を表示する。

### P2: 削除・危険操作の確認UIが統一されていない

会計系は `AppConfirm` を使う一方、商品削除、キャスト削除、領収書スキャンミス、設定のデータ削除はブラウザ標準 `confirm` を使っている。重要操作ほど文脈付きモーダルに寄せた方が、対象名、影響範囲、戻し方を確認しやすい。

根拠:

- `wwwroot/js/site.js:241`
- `wwwroot/js/features/business-checkout.js:44`
- `Pages/Management/Items.cshtml:201`
- `Pages/Management/Casts.cshtml:68`
- `Pages/Closing/Receipts.cshtml:170`
- `Pages/Settings/Index.cshtml:180`

推奨:

- 破壊的操作は `AppConfirm` に統一する。
- 商品名、キャスト名、領収書番号、削除対象テーブルなどを確認文に含める。
- 操作後の戻し方や影響範囲を短く出す。

### P2: 新規伝票モーダルで出勤キャスト取得失敗と「対象なし」が区別しづらい

出勤キャスト取得に失敗した場合、空配列にフォールバックする経路がある。通信失敗や権限不足と「出勤キャストがいない」がUI上近く見え、スタッフが原因を判断しづらい。

根拠:

- `wwwroot/js/features/create-slip-modal.js:64`
- `wwwroot/js/features/create-slip-modal.js:197`

推奨:

- 取得失敗時は「再取得」ボタン付きのエラー表示にする。
- 本当に出勤キャストがいない場合は勤怠画面への導線を出す。
- PageModel/Repository側も成功空配列と失敗を分離する。

### P2: 支払い手段が固定で、店舗運用の変化に弱い

会計支払い手段がJS側の固定値になっている。現金、CAT、PayPay以外の追加や廃止があると、設定ではなくコード修正が必要になる。

根拠:

- `wwwroot/js/features/business-checkout.js:223`

推奨:

- 支払い手段マスタを導入し、表示順、有効/無効、名称を管理画面から変更できるようにする。
- 会計モーダルはサーバーから渡された支払い手段を描画する。

### P3: 締めトップの個別パネルに明示的な再試行導線が弱い

締めトップの各パネルは自動更新されるが、失敗時に各パネル単位で押せる再試行ボタンが見当たらない。待てば直る設計より、現場では「この項目だけ再取得」がある方が原因切り分けしやすい。

根拠:

- `Pages/Closing/Index.cshtml:293`
- `Pages/Closing/Index.cshtml:489`

推奨:

- 各パネルに再取得ボタンと最終更新時刻を表示する。
- 失敗時は自動更新中か停止中かを明示する。

### P3: キャスト売上額調整の入力エラーが欄単位で見えにくい

金額バリデーションがModelOnlyエラー中心で、どの金額欄が問題か入力欄直下で把握しづらい可能性がある。

根拠:

- `Pages/Closing/CastSalesAdjustment.cshtml.cs:296`
- `Pages/Closing/CastSalesAdjustment.cshtml:146`

推奨:

- 行ごとの金額欄にエラーを紐づける。
- エラー時は該当行へスクロールまたはハイライトする。

### P3: 設定画面の注文端末モード説明が古い

設定画面で注文端末モードが「後続実装予定のオーダー登録専用画面」と説明されているが、`/Orders/Index` は既に実装されている。実装済み機能が未実装に見える。

根拠:

- `Pages/Settings/Index.cshtml:97`
- `Pages/Orders/Index.cshtml.cs:47`

推奨:

- 説明文を現在の挙動に合わせ、「注文登録専用画面を最初に表示する」に変更する。

### P3: モーダル内の未入力作業の閉じ忘れ確認が限定的

営業中伝票エディタでは自由入力明細の未保存時だけ閉じる確認がある。キャスト追加、指名追加、注文追加などの入力途中も、閉じた場合に入力が消える可能性がある。

根拠:

- `wwwroot/js/features/business-slip-editor.js:1044`
- `wwwroot/js/features/business-slip-editor.js:908`

推奨:

- 入力途中のフォームを検知し、閉じる前に確認する対象を広げる。
- localStorageへ一時入力を保持するか、モーダル内タブ切替でも入力を維持する。

## 優先改善順

1. 深夜帯の会計時刻初期値を修正し、営業日跨ぎテストを追加する。
2. 会計確定後の領収書印刷失敗をユーザーに通知し、再印刷導線を出す。
3. 商品マスタとキャスト管理に既存データ編集導線を追加する。
4. 危険操作の確認UIを `AppConfirm` に統一する。
5. 営業中画面と締めトップに明示的な再取得ボタン、最終更新時刻、失敗理由を出す。
6. 設定画面の古い説明文を現状に合わせて直す。
