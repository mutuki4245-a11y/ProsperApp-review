-- 空のDBを立ち上げる時だけ効く、最小の会社・部署。
--
-- store_rpc/00b_app_access.sql は初期管理者を department_id = 1 に紐付けるため、
-- 会社と部署が1件も無いDBではFK違反で止まります。本番はこの2テーブルに実データが
-- あるので、「1件も無いときだけ入れる」という条件で本番への影響を断ちます。
--
-- コードと名称を本番のcompany_id=1 / department_id=1に合わせてあるのは、
-- Sql/ 配下のseed（商品マスタ、テーブルマスタ、領収書発行者）が
-- department_name = 'mieu本店' を前提に書かれているためです。名前を変えると
-- テスト用DBにカタログを入れられず、レビューで触れる画面が空になります。
-- 環境の取り違えはデータ名ではなく、アプリ側のテスト環境バナー
-- （appsettings.Staging.json の App:EnvironmentBanner）で防ぎます。

do $$
declare
    v_company_id bigint;
begin
    if exists (select 1 from public.company_master) then
        return;
    end if;

    insert into public.company_master (company_id, company_code, company_name)
        overriding system value
    values (1, 'PROSPER', '合同会社Prosper')
    returning company_id into v_company_id;

    -- identity列を明示指定したので、次の自動採番が衝突しないよう進めます。
    perform setval(
        pg_get_serial_sequence('public.company_master', 'company_id'),
        greatest(v_company_id, 1)
    );

    if not exists (select 1 from public.department_master) then
        insert into public.department_master (department_id, company_id, department_code, department_name)
            overriding system value
        values (1, v_company_id, 'MIEU_HONTEN', 'mieu本店');

        perform setval(
            pg_get_serial_sequence('public.department_master', 'department_id'),
            1
        );
    end if;
end;
$$;
