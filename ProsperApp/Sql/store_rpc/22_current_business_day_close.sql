-- 営業日締めは、事前の current/readiness 読取を行わず、DB内の整合性検証と状態変更を1 RPCで完結します。

begin;

drop function if exists store.close_current_business_day(bigint, bigint, text, text, boolean);

create or replace function store.close_current_business_day(
    p_department_id bigint,
    p_expected_business_day_id bigint default null,
    p_memo text default null,
    p_pending_receipt_status text default 'unprocessed',
    p_ignore_closing_requirements boolean default false
)
returns table (
    business_day_id bigint,
    company_id bigint,
    department_id bigint,
    business_date date,
    opened_at timestamp with time zone,
    closed_at timestamp with time zone,
    status text,
    memo text
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day_id bigint;
begin
    select current_business_day.business_day_id
      into v_business_day_id
      from store.get_current_business_day(p_department_id) current_business_day;

    if v_business_day_id is null then
        raise exception 'business_day_not_open';
    end if;

    if p_expected_business_day_id is null or p_expected_business_day_id <> v_business_day_id then
        raise exception 'business_day_revision_conflict';
    end if;

    return query
    select *
      from store.close_business_day(
          p_department_id,
          v_business_day_id,
          p_memo,
          p_pending_receipt_status,
          p_ignore_closing_requirements);
end;
$$;

revoke all on function store.close_current_business_day(bigint, bigint, text, text, boolean)
    from public, anon, authenticated, service_role;

commit;
