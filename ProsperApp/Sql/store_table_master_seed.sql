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
    sort_order,
    is_active
)
select
    d.company_id,
    d.department_id,
    t.table_code,
    null,
    t.sort_order,
    true
from public.department_master d
cross join (
    values
        ('A1', 101),
        ('A2', 102),
        ('A3', 103),
        ('A4', 104),
        ('A5', 105),
        ('A6', 106),
        ('B1', 201),
        ('B2', 202),
        ('B3', 203),
        ('B4', 204),
        ('B5', 205),
        ('B6', 206),
        ('C1', 301),
        ('C2', 302),
        ('C3', 303),
        ('C4', 304),
        ('C5', 305),
        ('C6', 306)
) as t(table_code, sort_order)
where d.department_name = 'mieu本店'
  and d.is_active = true
on conflict on constraint uq_store_table_master_code
do update set
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
