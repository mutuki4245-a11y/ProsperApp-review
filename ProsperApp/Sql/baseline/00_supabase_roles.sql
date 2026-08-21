-- Supabaseが用意する公開ロールのbaseline定義。
--
-- リポジトリのSQLは至るところで `revoke ... from public, anon, authenticated,
-- service_role` を実行します。Supabase上ではこの3ロールが最初から存在しますが、
-- 素のPostgreSQL（CIのservice containerやローカル検証）には無いため、
-- revokeが `role "anon" does not exist` で落ちます。
--
-- ここで不足分だけを作り、権限剥奪が同じように効くようにします。Supabaseへ
-- 流した場合は3つとも既にあるので no-op です。ログイン不可・権限なしで作るので、
-- このファイル自体が新しいアクセス経路を作ることはありません。

do $$
declare
    role_name text;
begin
    foreach role_name in array array['anon', 'authenticated', 'service_role']
    loop
        if not exists (select 1 from pg_roles where rolname = role_name) then
            execute format('create role %I nologin noinherit', role_name);
        end if;
    end loop;
end;
$$;
