-- 商品管理のシステム商品カテゴリ統合。
-- カラオケ、料金用の自動商品、カテゴリ未設定の商品を「システム」カテゴリへ移します。
-- 実行後の戻し方: 対象商品の item_category_id を旧カテゴリIDへ更新し、
-- category_code = 'karaoke' のカテゴリを再有効化します。注文行のスナップショットは変更しません。

begin;

insert into public.store_item_category_master (
    company_id,
    department_id,
    category_code,
    category_name,
    sort_order,
    is_active
)
select
    d.company_id,
    d.department_id,
    'system',
    'システム',
    9010,
    true
from public.department_master d
where d.is_active = true
on conflict (company_id, department_id, category_code)
do update
   set category_name = excluded.category_name,
       sort_order = excluded.sort_order,
       is_active = true,
       updated_at = now();

with system_categories as (
    select
        c.item_category_id,
        c.company_id,
        c.department_id
    from public.store_item_category_master c
    where c.category_code = 'system'
)
update public.store_item_master i
   set item_category_id = sc.item_category_id,
       updated_at = now()
  from system_categories sc
 where sc.company_id = i.company_id
   and sc.department_id = i.department_id
   and (
       i.item_type <> 'standard'
       or i.item_category_id is null
   );

update public.store_item_category_master c
   set is_active = false,
       updated_at = now()
 where c.category_code = 'karaoke'
   and not exists (
       select 1
       from public.store_item_master i
       where i.item_category_id = c.item_category_id
   );

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

commit;
