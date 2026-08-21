# 本番Supabaseプロジェクトの同居アプリ

本番プロジェクト `zwdecfoecgpzpkallukh` は、ProsperApp専用ではありません。
2026-08-21から、別アプリのスキーマが2つ同居しています。

| スキーマ | アプリ | リポジトリ | 定義の正本 |
|---|---|---|---|
| `public` / `store` / `accounting` | ProsperApp | ProsperApp | `Sql/` |
| `nightqueen_gp` | NightQueenGP | NightQueenGP | 同リポジトリの `supabase/schema.sql` |
| `cast_race` | CastRaceApp | CastRaceApp | 同リポジトリの `supabase/schema.sql` |

`Sql/apply_all.sh` はProsperAppのスキーマだけを扱います。同居分は対象外です。

## なぜ同居しているか

freeプランは**1ユーザーあたり同時2プロジェクト**までで、本番と
`cast_race_newbee` で埋まっていました。テスト用DBのプロジェクト枠を空けるため、
`cast_race_newbee` の中身をこのプロジェクトへ移し、旧プロジェクトを一時停止しました。

一時停止したプロジェクトは枠を消費しません。旧プロジェクトは削除していないので、
データはそのまま残っています。

## DB作業時の注意

**ProsperAppの作業が同居スキーマを壊さないこと。** 特に次を確認してください。

- `public` スキーマ全体への `revoke` / `grant` を書かない。ProsperAppのSQLは
  対象テーブルを個別に指定しています。この方針を崩さないでください。
- `drop schema ... cascade` を `cast_race` / `nightqueen_gp` に対して実行しない。
- 本番のダンプ・リストアは同居分も含めて扱う。ProsperAppのテーブルだけを
  戻すと、他の2アプリが壊れます。

**anonキーの意味が変わりました。** 移管前、本番プロジェクトにはanon向けの
RLSポリシーが1件もなく、anonキーでは何も読めませんでした。現在は
`cast_race` と `nightqueen_gp` のデータがanonキーで読めます。
ProsperAppのテーブルは従来どおりanonから到達できません
（`Sql/store_rpc/99_grants.sql` と各テーブルのRLS）。

## 同居分の定義はこのリポジトリにありません

2つとも、それぞれのリポジトリの `supabase/schema.sql` が正本です。どちらも
冒頭に共有プロジェクト前提であることが書いてあります。ProsperApp側に複製は
置きません。二重管理になり、いずれ食い違うためです。

## 移管時に何をしたか

1. 各リポジトリの `supabase/schema.sql` を本番へ適用
2. 旧プロジェクトのPostgREST経由でデータを取得し、本番へupsert
   （一時的に `http` 拡張を入れ、完了後に `drop extension http` で撤去）
3. 全9テーブルについて、件数と内容のハッシュが移管元と一致することを確認
4. 各アプリの接続先を本番プロジェクトへ変更
5. `cast_race_newbee` を一時停止（**削除していない**）
