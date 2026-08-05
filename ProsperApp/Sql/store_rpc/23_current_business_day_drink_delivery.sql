-- 納品額保存は、営業日読取/作成と保存を分けず1 mutationで完結します。

begin;

drop function if exists store.save_current_business_day_drink_delivery_amount_v2(bigint, bigint, date, numeric);

create or replace function store.save_current_business_day_drink_delivery_amount_v2(
    p_department_id bigint,
    p_expected_business_day_id bigint default null,
    p_business_date date default null,
    p_drink_delivery_amount numeric default null
)
returns table (
    business_day jsonb,
    drink_delivery_amount numeric,
    is_entered boolean
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_amount numeric;
begin
    if p_department_id <= 0 or p_business_date is null then
        raise exception 'invalid_drink_delivery_input';
    end if;

    perform pg_advisory_xact_lock(hashtextextended(
        format('store_current_drink_delivery:%s', p_department_id),
        0));

    select current_business_day.*
      into v_business_day
      from store.get_current_business_day(p_department_id) current_business_day;

    if v_business_day.business_day_id is null then
        if p_expected_business_day_id is not null then
            raise exception 'business_day_revision_conflict';
        end if;

        select opened_business_day.*
          into v_business_day
          from store.open_business_day(p_department_id, p_business_date, null) opened_business_day;
    else
        if p_expected_business_day_id is not null and p_expected_business_day_id <> v_business_day.business_day_id then
            raise exception 'business_day_revision_conflict';
        end if;

        if v_business_day.business_date <> p_business_date then
            raise exception 'business_day_closing_required';
        end if;
    end if;

    select store.save_business_day_drink_delivery_amount(
        p_department_id,
        v_business_day.business_day_id,
        p_drink_delivery_amount)
      into v_amount;

    return query
    select to_jsonb(v_business_day), v_amount, true;
end;
$$;

revoke all on function store.save_current_business_day_drink_delivery_amount_v2(bigint, bigint, date, numeric)
    from public, anon, authenticated, service_role;

commit;
