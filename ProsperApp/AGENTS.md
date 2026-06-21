# AGENTS.md

## 基本方針

このリポジトリでは、メインCodexのトークン消費を抑えるため、単純調査・低リスク編集・軽量レビューをサブエージェントへ委任できる。

メインCodexは最終判断、設計判断、高リスク変更、SQL実行判断、デプロイ判断を担当する。サブエージェントは明確に限定された作業だけを行う。

このリポジトリでは、AGENTS.mdの基準に従って、必要なら明示確認なしでサブエージェントを使ってよい。

ただし、サブエージェントを使う場合も、メインCodexはこのAGENTS.mdの権限分離、禁止事項、並列実行制限を守る。

## Windows PowerShellでの文字コード

PowerShell 5.1では日本語を含むUTF-8ファイルが文字化けすることがある。

- 日本語を含むファイルを読む場合は、必要に応じてUTF-8を明示する。
- 日本語を含むファイルを書く場合は、既存の文字コードと改行を維持する。
- 文字化けした内容をそのまま再利用しない。
- TOML、Razor、C#、SQLの日本語文言はUTF-8として扱う。

## サブエージェント構成

### code_mapper

目的:
- 実装前のリポジトリ調査。
- Razor Pages、PageModel、Service、Model、SQL、設定キー、ルート、RPC名の洗い出し。
- 既存挙動と依存関係の整理。

権限:
- `read-only`

使う場面:
- 関連ファイルが多そうな変更の前。
- 既存仕様や影響範囲を安く把握したい時。
- メインCodexが編集前に探索コストを下げたい時。

禁止:
- ファイル編集。
- SQL実行、マイグレーション実行。
- 最終的な設計判断。
- 大規模リライト提案。

### simple_editor

目的:
- メインCodexが明示した、狭い範囲の低リスク編集。

権限:
- `workspace-write`

任せてよい作業:
- 文字化けや日本語UI文言の修正。
- ラベル、見出し、ボタン、バリデーション文言の変更。
- 明確に指示された小規模な機械的変更。
- 意図が明確な単純コンパイルエラー修正。
- TOML、JSON、CSS、Razor、コメントの明示的な更新。

禁止:
- SQL/RPCスキーマや関数設計の変更。
- 認証、認可、RLS、OAuth、Google Drive権限、秘密情報まわりの変更。
- 会計、伝票、会計処理、営業日、領収書、給与などの業務ロジック変更。
- destructiveコマンド。
- SQL実行、マイグレーション実行。
- 広範なリファクタ。
- 指示されていない挙動の追加。

運用:
- 編集前に、対象ファイルと変更内容を明示させる。
- 曖昧または高リスクなら停止させる。
- 編集後は変更ファイル、未確認事項、テスト実行有無を報告させる。

### sql_rpc_checker

目的:
- Supabase SQL/RPCとC# Repositoryの互換性確認。

権限:
- `read-only`

確認項目:
- C#側RPC名とSQL関数名の一致。
- JSON payloadのキーとSQL引数名の一致。
- SQLの戻り列名とC#パーサの一致。
- `security definer` 関数の `set search_path = public`。
- `grant execute` の有無。
- 店舗スコープ処理の `department_id` フィルタ。
- 更新系RPCの対象IDと `department_id` 絞り込み。
- Postgresの型、構文、戻り値リスク。

禁止:
- SQL編集。
- SQL実行。
- 不要なDB再設計提案。
- 秘密情報の要求や露出。

### ui_copy_fixer

目的:
- 日本語UI文言と文字化け修正。

権限:
- `workspace-write`

任せてよい作業:
- Razor Pages、PageModel、ラベル、見出し、バリデーション、ボタン文言の文字化け修正。
- 日本語文言の統一。
- CSSクラス、route、handler、bindingを変えない範囲の文言修正。

禁止:
- 業務ロジック変更。
- model property、handler名、route名、RPC名、validation ruleの変更。
- SQL、認証、Drive、Supabase Repository、秘密情報の変更。
- 文言修正を超えるUI設計変更。

### build_log_triager

目的:
- ビルド、テスト、コンパイルエラーのログ整理。

権限:
- `read-only`

使う場面:
- `dotnet build` やテスト失敗後。
- エラーが多く、原因別にまとめたい時。

出力させる内容:
- エラー概要。
- root cause別の分類。
- 修正対象ファイルやシンボル。
- 不確実な点。

禁止:
- ファイル編集。
- 明示指示なしのコマンド実行。
- ログにない推測。

### light_reviewer

目的:
- 実装後の低コスト軽量レビュー。

権限:
- `read-only`

確認項目:
- 明らかな実行時エラー。
- Razor handler、model binding、DI、route、config keyの不一致。
- RPC名、引数、戻り値の不一致。
- バリデーション不足。
- unsafe assumption。
- ユーザー向け文字化け。
- status名や設定名の不整合。

禁止:
- ファイル編集。
- 大規模リライト提案。
- 関係ない設計レビュー。
- リポジトリで確認できない事実の断定。

## メインCodexが必ず担当すること

以下はサブエージェントへ丸投げしない。

- 最終的な設計判断。
- DBスキーマ/RPC設計の採否。
- Supabase RLS、grant、security definer方針。
- 認証、認可、OAuth、Google Drive権限。
- 会計、伝票、会計処理、営業日、領収書、給与などの業務ロジック。
- SQL実行、マイグレーション実行。
- Azureデプロイや本番環境変数の判断。
- テスト結果の最終評価。

## 推奨ワークフロー

### Git運用

`main` は安定版として扱い、タスクごとに作業ブランチを作る。

基本手順:

1. `git switch main`
2. `git pull`
3. `git switch -c task/<task-name>`
4. 対象タスクを実装する。
5. `dotnet build --no-restore` など、必要な確認を実行する。
6. 意味のある単位で `git add` / `git commit` する。
7. 新規ブランチなら `git push -u origin task/<task-name>`、既存ブランチなら `git push` でリモートへ反映する。
8. 確認できたら `main` に戻って `git merge task/<task-name>` する。
9. `main` のビルドまたは必要な確認を行い、問題なければ `git push origin main` でリモートへ反映する。
10. merge後、不要なら `git branch -d task/<task-name>` で作業ブランチを削除する。

一人開発でも、認証、SQL/RPC、画面構成、構造整理など影響範囲が広い変更は、直接 `main` に積まずタスクブランチで作業する。

コミットの考え方:

- 1コミットは、ひとことで説明できる変更単位にする。
- 保存ごとではなく、ビルドや最低限の確認ができた区切りでコミットする。
- `bin/`、`obj/`、一時ビルド出力、秘密情報を含むローカル設定はコミットしない。
- 複数テーマが混ざった場合は、後で追いやすいようにコミットを分ける。
- ローカルだけに残す明確な理由がない限り、タスク完了時はpushまで行い、GitHub上でブランチやコミットを確認できる状態にする。

大きな変更の例:

1. 認証やルート整理。
2. 画面やPageModelの変更。
3. RepositoryやInfrastructureの構造整理。
4. SQL/RPC変更とgrant更新。
5. HANDOFFや参照SQLの更新。

通常はタスクブランチの最終状態でビルドを通してからpushし、`main` にmergeする。チーム運用では、タスクブランチをpushしてPull Requestを作り、レビューとCI確認後に `main` へmergeする。merge後の `main` もpushまで行い、GitHub上の状態を最新にする。

### Codex作業

複数ファイルにまたがる変更:

1. `code_mapper` で関連範囲を調査する。
2. メインCodexが設計と編集方針を決める。
3. 低リスク文言修正なら `ui_copy_fixer`、単純編集なら `simple_editor` に限定委任する。
4. SQL/RPCが絡む場合は `sql_rpc_checker` で互換性確認する。
5. メインCodexがビルドまたはテストを実行する。
6. 失敗時は `build_log_triager` でログを整理する。
7. 実装後に `light_reviewer` で軽量レビューする。
8. メインCodexが最終修正と最終報告を行う。

## 並列実行の制限

`.codex/config.toml` の設定に従う。

- `max_threads = 3`
- `max_depth = 1`

サブエージェントからさらにサブエージェントを呼ばせない。並列化は、調査・レビュー・ログ整理など独立した作業に限定する。

## 判断基準

サブエージェントへ任せるか迷う場合:

- 失敗しても簡単に戻せる単純作業なら任せてよい。
- DB、認証、権限、業務ロジック、本番影響があるならメインCodexが担当する。
- 編集範囲を1文で正確に指定できないなら任せない。
