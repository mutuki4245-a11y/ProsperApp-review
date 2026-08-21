begin;

do $$
begin
    if not exists (
        select 1
        from public.department_master d
        where d.department_name = 'mieu本店'
          and d.is_active = true
    ) then
        raise exception 'department_not_found:mieu本店';
    end if;
end;
$$;

insert into public.store_table_master (
    company_id,
    department_id,
    table_code,
    table_name,
    table_category_no,
    sort_order,
    is_active
)
select
    d.company_id,
    d.department_id,
    t.table_code,
    null,
    t.table_category_no,
    t.sort_order,
    true
from public.department_master d
cross join (
    values
        ('A1', 1, 101),
        ('A2', 1, 102),
        ('A3', 1, 103),
        ('A4', 1, 104),
        ('A5', 1, 105),
        ('A6', 1, 106),
        ('B1', 2, 201),
        ('B2', 2, 202),
        ('B3', 2, 203),
        ('B4', 2, 204),
        ('B5', 2, 205),
        ('B6', 2, 206),
        ('C1', 3, 301),
        ('C2', 3, 302),
        ('C3', 3, 303),
        ('C4', 3, 304),
        ('C5', 3, 305),
        ('C6', 3, 306)
) as t(table_code, table_category_no, sort_order)
where d.department_name = 'mieu本店'
  and d.is_active = true
on conflict on constraint uq_store_table_master_code
do update set
    table_category_no = excluded.table_category_no,
    sort_order = excluded.sort_order,
    is_active = true,
    updated_at = now();

update public.store_table_master stm
set
    is_active = false,
    updated_at = now()
from public.department_master d
where d.department_id = stm.department_id
  and d.company_id = stm.company_id
  and d.department_name = 'mieu本店'
  and d.is_active = true
  and stm.table_code in ('1', '2', '3', '4', '5', '6');

commit;
