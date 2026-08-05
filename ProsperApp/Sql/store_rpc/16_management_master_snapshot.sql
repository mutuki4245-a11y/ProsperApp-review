begin;

drop function if exists store.get_management_master_snapshot(bigint, text);

create or replace function store.get_management_master_snapshot(
    p_department_id bigint,
    p_known_revision text default null
)
returns table (
    master_revision text,
    unchanged boolean,
    snapshot jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_snapshot jsonb;
    v_revision text;
begin
    if not exists (
        select 1
          from department_master department
         where department.department_id = p_department_id
           and department.is_active = true
    ) then
        raise exception 'store_department_not_found';
    end if;

    select jsonb_build_object(
        'tables', coalesce((
            select jsonb_agg(to_jsonb(table_row) order by table_row.table_category_no, table_row.sort_order, table_row.table_code)
              from store.get_table_admin_list(p_department_id) table_row
        ), '[]'::jsonb),
        'casts', coalesce((
            select jsonb_agg(to_jsonb(cast_row) order by cast_row.display_name, cast_row.cast_id)
              from store.get_casts_admin(p_department_id) cast_row
        ), '[]'::jsonb),
        'staffs', coalesce((
            select jsonb_agg(to_jsonb(staff_row) order by staff_row.display_name, staff_row.staff_id)
              from store.get_staffs_admin(p_department_id) staff_row
        ), '[]'::jsonb),
        'itemCatalog', coalesce((
            select jsonb_agg(to_jsonb(item_row) order by item_row.row_type, item_row.sort_order, item_row.item_category_id, item_row.item_id)
              from store.get_item_admin_catalog(p_department_id) item_row
        ), '[]'::jsonb),
        'nominationBacks', coalesce((
            select jsonb_agg(to_jsonb(nomination_row) order by nomination_row.sort_order, nomination_row.nomination_kind)
              from store.get_nomination_back_master(p_department_id) nomination_row
        ), '[]'::jsonb),
        'pricingPlan', coalesce((
            select to_jsonb(pricing_row)
              from store.get_pricing_plan(p_department_id) pricing_row
        ), '{}'::jsonb)
    )
      into v_snapshot;

    v_revision := md5(v_snapshot::text);

    return query
    select
        v_revision,
        nullif(trim(coalesce(p_known_revision, '')), '') = v_revision,
        case
            when nullif(trim(coalesce(p_known_revision, '')), '') = v_revision then null
            else v_snapshot
        end;
end;
$$;

revoke all on function store.get_management_master_snapshot(bigint, text)
    from public, anon, authenticated, service_role;

commit;
