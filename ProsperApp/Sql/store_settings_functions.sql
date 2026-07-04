begin;

create schema if not exists store;

drop function if exists public.get_store_departments();
drop function if exists store.get_departments();

create or replace function store.get_departments()
returns table (
    department_id bigint,
    company_id bigint,
    department_code text,
    department_name text,
    is_active boolean
)
language sql
security definer
set search_path = public
as $$
    select
        d.department_id,
        d.company_id,
        d.department_code,
        d.department_name,
        d.is_active
    from public.department_master d
    where d.is_active = true
    order by d.company_id asc, d.department_code asc, d.department_id asc;
$$;

revoke execute on function store.get_departments() from public, anon, authenticated, service_role;

commit;
