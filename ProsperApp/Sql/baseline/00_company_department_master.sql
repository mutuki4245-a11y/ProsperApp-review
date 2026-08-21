-- 会社・部署マスタのbaseline定義。
--
-- これらのテーブルはProsperApp導入以前からSupabaseに存在していたため、これまで
-- リポジトリのSQLは「既にある」前提で参照するだけでした。空のPostgreSQLへ
-- Sql/ 一式を流してテスト用DBを作れるようにするため、導入時点の姿をここに残します。
--
-- ProsperAppが後から足した列（invoice_registration_number、receipt_* 、
-- attendance_minute_step など）はここには含めません。それらは
-- store_order_accounting_tables.sql の alter table が引き続き担当します。
-- 本番へ流しても create table if not exists が全て no-op になります。

begin;

create table if not exists public.company_master (
    company_id bigint generated always as identity primary key,
    company_code text not null unique,
    company_name text not null,
    is_active boolean not null default true,
    created_at timestamp with time zone not null default now(),
    company_short_code text
);

create table if not exists public.department_master (
    department_id bigint generated always as identity primary key,
    company_id bigint not null references public.company_master(company_id),
    department_code text not null,
    department_name text not null,
    is_active boolean not null default true,
    constraint department_master_company_id_department_code_key
        unique (company_id, department_code)
);

alter table public.company_master enable row level security;
alter table public.department_master enable row level security;

commit;
