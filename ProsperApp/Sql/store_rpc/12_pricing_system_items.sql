-- セット・延長料金を、会計時に生成する編集不可のシステム商品として扱います。
-- 料金計算の正本は store_slip_pricing_lines に残し、注文行は帳票・明細表示用です。

alter table public.store_item_master
    drop constraint if exists chk_store_item_master_type;

alter table public.store_item_master
    add constraint chk_store_item_master_type
    check (item_type in ('standard', 'karaoke', 'nomination_fee', 'set_fee', 'extension_fee'));

alter table public.store_order_lines
    drop constraint if exists chk_store_order_lines_source_type;

alter table public.store_order_lines
    add constraint chk_store_order_lines_source_type
    check (source_type is null or source_type in ('nomination_fee', 'automatic_pricing'));

create unique index if not exists ux_store_item_master_set_fee_active
    on public.store_item_master(company_id, department_id)
    where item_type = 'set_fee' and is_active = true;

create unique index if not exists ux_store_item_master_extension_fee_active
    on public.store_item_master(company_id, department_id)
    where item_type = 'extension_fee' and is_active = true;

create or replace function store.ensure_pricing_system_items(p_department_id bigint)
returns table (
    pricing_code text,
    item_id bigint
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_company_id bigint;
    v_system_item_category_id bigint;
begin
    select d.company_id
      into v_company_id
      from public.department_master d
     where d.department_id = p_department_id
       and d.is_active = true;

    if v_company_id is null then
        raise exception 'store_department_not_found';
    end if;

    insert into public.store_item_category_master (
        company_id,
        department_id,
        category_code,
        category_name,
        sort_order,
        is_active
    )
    values (
        v_company_id,
        p_department_id,
        'system',
        'システム',
        9010,
        true
    )
    on conflict (company_id, department_id, category_code)
    do update
       set category_name = excluded.category_name,
           sort_order = excluded.sort_order,
           is_active = true,
           updated_at = now()
    returning item_category_id into v_system_item_category_id;

    insert into public.store_item_master (
        company_id,
        department_id,
        item_category_id,
        item_name,
        item_type,
        default_price,
        is_cast_back_target,
        cast_back_unit_amount,
        cast_back_regular_unit_amount,
        cast_back_nomination_unit_amount,
        cast_back_type,
        sort_order,
        is_active
    )
    values
        (v_company_id, p_department_id, v_system_item_category_id, '__system_set_fee__', 'set_fee', 0, false, 0, 0, 0, 'other', -1000, true),
        (v_company_id, p_department_id, v_system_item_category_id, '__system_extension_fee__', 'extension_fee', 0, false, 0, 0, 0, 'other', -999, true)
    on conflict (company_id, department_id, item_name) do update
       set item_category_id = excluded.item_category_id,
           item_type = excluded.item_type,
           default_price = 0,
           is_cast_back_target = false,
           cast_back_unit_amount = 0,
           cast_back_regular_unit_amount = 0,
           cast_back_nomination_unit_amount = 0,
           cast_back_type = 'other',
           is_active = true,
           updated_at = now();

    return query
    select
        case i.item_type when 'set_fee' then 'set' else 'extension' end,
        i.item_id
    from public.store_item_master i
    where i.company_id = v_company_id
      and i.department_id = p_department_id
      and i.item_type in ('set_fee', 'extension_fee')
      and i.is_active;
end;
$$;

revoke execute on function store.ensure_pricing_system_items(bigint) from public, anon, authenticated, service_role;
