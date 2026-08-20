begin;

create table if not exists store.management_master_operation_results (
    department_id bigint not null,
    operation_id text not null,
    request_body jsonb not null,
    response jsonb not null,
    created_at timestamp with time zone not null default now(),
    primary key (department_id, operation_id)
);

revoke all on table store.management_master_operation_results from public, anon, authenticated, service_role;
create index if not exists management_master_operation_results_created_at_idx
    on store.management_master_operation_results (created_at);

drop function if exists store.get_management_master_state(bigint);

create or replace function store.get_management_master_state(
    p_department_id bigint
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_tables jsonb;
    v_casts jsonb;
    v_staffs jsonb;
    v_items jsonb;
    v_nominations jsonb;
    v_pricing jsonb;
    v_revisions jsonb;
begin
    if not exists (
        select 1
          from public.department_master department
         where department.department_id = p_department_id
           and department.is_active = true
    ) then
        raise exception 'store_department_not_found';
    end if;

    select coalesce(jsonb_agg(to_jsonb(table_row) order by table_row.table_category_no, table_row.sort_order, table_row.table_code), '[]'::jsonb)
      into v_tables
      from store.get_table_admin_list(p_department_id) table_row;

    select coalesce(jsonb_agg(to_jsonb(cast_row) order by cast_row.display_name, cast_row.cast_id), '[]'::jsonb)
      into v_casts
      from store.get_casts_admin(p_department_id) cast_row;

    select coalesce(jsonb_agg(to_jsonb(staff_row) order by staff_row.display_name, staff_row.staff_id), '[]'::jsonb)
      into v_staffs
      from store.get_staffs_admin(p_department_id) staff_row;

    select coalesce(jsonb_agg(to_jsonb(item_row) order by item_row.row_type, item_row.sort_order, item_row.item_category_id, item_row.item_id), '[]'::jsonb)
      into v_items
      from store.get_item_admin_catalog(p_department_id) item_row;

    select coalesce(jsonb_agg(to_jsonb(nomination_row) order by nomination_row.sort_order, nomination_row.nomination_kind), '[]'::jsonb)
      into v_nominations
      from store.get_nomination_back_master(p_department_id) nomination_row;

    select coalesce(to_jsonb(pricing_row), '{}'::jsonb)
      into v_pricing
      from store.get_pricing_plan(p_department_id) pricing_row;

    v_pricing := coalesce(v_pricing, '{}'::jsonb);
    v_revisions := jsonb_build_object(
        'tables', md5(v_tables::text),
        'casts', md5(v_casts::text),
        'staffs', md5(v_staffs::text),
        'items', md5(v_items::text),
        'nominations', md5(v_nominations::text),
        'pricing', md5(v_pricing::text)
    );

    return jsonb_build_object(
        'tables', v_tables,
        'casts', v_casts,
        'staffs', v_staffs,
        'itemCatalog', v_items,
        'nominationBacks', v_nominations,
        'pricingPlan', v_pricing,
        'revisions', v_revisions
    );
end;
$$;

revoke all on function store.get_management_master_state(bigint)
    from public, anon, authenticated, service_role;

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
    v_snapshot := store.get_management_master_state(p_department_id);
    v_revision := md5((v_snapshot->'revisions')::text);

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

drop function if exists store.save_management_master_v2(bigint, text, text, text, text, jsonb, boolean);

create or replace function store.save_management_master_v2(
    p_department_id bigint,
    p_operation_id text,
    p_area text,
    p_action text,
    p_expected_area_revision text,
    p_payload jsonb default '{}'::jsonb,
    p_allow_admin_actions boolean default false
)
returns table (
    status text,
    master_revision text,
    area_revision text,
    area text,
    master_delta jsonb,
    error_code text,
    error_message text
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_operation_id text := lower(trim(coalesce(p_operation_id, '')));
    v_area text := lower(trim(coalesce(p_area, '')));
    v_action text := lower(trim(coalesce(p_action, '')));
    v_request_body jsonb;
    v_existing_request jsonb;
    v_response jsonb;
    v_state jsonb;
    v_area_key text;
    v_current_area_revision text;
    v_delta jsonb;
    v_status text := 'confirmed';
    v_error_code text;
    v_error_message text;
begin
    if p_department_id <= 0 or
       v_operation_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' or
       jsonb_typeof(coalesce(p_payload, '{}'::jsonb)) <> 'object' then
        raise exception 'invalid_management_master_operation';
    end if;

    v_area_key := case v_area
        when 'tables' then 'tables'
        when 'casts' then 'casts'
        when 'staffs' then 'staffs'
        when 'items' then 'itemCatalog'
        when 'nominations' then 'nominationBacks'
        when 'pricing' then 'pricingPlan'
        else null
    end;
    if v_area_key is null then
        raise exception 'invalid_management_master_area';
    end if;

    v_request_body := jsonb_build_object(
        'area', v_area,
        'action', v_action,
        'expected_area_revision', p_expected_area_revision,
        'payload', coalesce(p_payload, '{}'::jsonb),
        'allow_admin_actions', coalesce(p_allow_admin_actions, false)
    );

    perform pg_advisory_xact_lock(hashtextextended(
        format('store_management_master:%s:%s', p_department_id, v_area),
        0));

    select operation.request_body, operation.response
      into v_existing_request, v_response
      from store.management_master_operation_results operation
     where operation.department_id = p_department_id
       and operation.operation_id = v_operation_id;

    if found then
        if v_existing_request <> v_request_body then
            raise exception 'management_master_operation_id_reused';
        end if;

        return query select
            v_response->>'status',
            v_response->>'master_revision',
            v_response->>'area_revision',
            v_response->>'area',
            v_response->'master_delta',
            v_response->>'error_code',
            v_response->>'error_message';
        return;
    end if;

    v_state := store.get_management_master_state(p_department_id);
    v_current_area_revision := v_state->'revisions'->>v_area;

    if nullif(trim(coalesce(p_expected_area_revision, '')), '') is null or
       p_expected_area_revision <> v_current_area_revision then
        v_status := 'conflict';
        v_error_code := 'management_master_revision_conflict';
        v_error_message := '別の端末で更新されています。入力内容を確認して再保存してください。';
    else
        begin
            case v_area
                when 'tables' then
                    case v_action
                        when 'save' then
                            perform 1 from store.upsert_table_internal(
                                p_department_id,
                                nullif(p_payload->>'table_id', '')::bigint,
                                p_payload->>'table_code',
                                p_payload->>'table_name',
                                coalesce(nullif(p_payload->>'table_category_no', '')::integer, 0),
                                coalesce(nullif(p_payload->>'sort_order', '')::integer, 0),
                                coalesce((p_payload->>'is_active')::boolean, true));
                        when 'delete' then
                            perform 1 from store.delete_table_internal(p_department_id, nullif(p_payload->>'table_id', '')::bigint);
                        else raise exception 'invalid_management_master_action';
                    end case;
                when 'casts' then
                    case v_action
                        when 'create' then
                            perform 1 from store.create_cast_internal(p_department_id, p_payload->>'display_name', p_payload->>'drink_memo');
                        when 'update_drink_memo' then
                            perform 1 from store.update_cast_drink_memo_internal(p_department_id, nullif(p_payload->>'cast_id', '')::bigint, p_payload->>'drink_memo');
                        when 'delete' then
                            perform 1 from store.delete_cast_internal(p_department_id, nullif(p_payload->>'cast_id', '')::bigint);
                        else raise exception 'invalid_management_master_action';
                    end case;
                when 'staffs' then
                    case v_action
                        when 'create' then
                            perform 1 from store.create_staff_internal(p_department_id, p_payload->>'display_name', coalesce(p_payload->>'employment_type', 'employee'));
                        when 'update_employment_type' then
                            perform 1 from store.update_staff_employment_type_internal(p_department_id, nullif(p_payload->>'staff_id', '')::bigint, p_payload->>'employment_type');
                        when 'delete' then
                            perform 1 from store.delete_staff_internal(p_department_id, nullif(p_payload->>'staff_id', '')::bigint);
                        else raise exception 'invalid_management_master_action';
                    end case;
                when 'items' then
                    if v_action in ('save_category', 'delete_category') and not coalesce(p_allow_admin_actions, false) then
                        raise exception 'management_admin_required';
                    end if;
                    case v_action
                        when 'save_category' then
                            perform 1 from store.upsert_item_category_internal(
                                p_department_id,
                                nullif(p_payload->>'item_category_id', '')::bigint,
                                p_payload->>'category_code',
                                p_payload->>'category_name',
                                coalesce(nullif(p_payload->>'sort_order', '')::integer, 0),
                                coalesce((p_payload->>'is_active')::boolean, true));
                        when 'delete_category' then
                            perform 1 from store.delete_item_category_internal(p_department_id, nullif(p_payload->>'item_category_id', '')::bigint);
                        when 'save_item' then
                            perform 1 from store.upsert_item_internal(
                                p_department_id,
                                nullif(p_payload->>'item_id', '')::bigint,
                                nullif(p_payload->>'item_category_id', '')::bigint,
                                p_payload->>'item_name',
                                coalesce(nullif(p_payload->>'default_price', '')::numeric, 0),
                                coalesce((p_payload->>'is_active')::boolean, true),
                                coalesce((p_payload->>'is_cast_back_target')::boolean, false),
                                coalesce(nullif(p_payload->>'cast_back_regular_unit_amount', '')::numeric, 0),
                                coalesce(nullif(p_payload->>'cast_back_nomination_unit_amount', '')::numeric, 0),
                                coalesce(p_payload->>'cast_back_type', 'drink'));
                        when 'delete_item' then
                            perform 1 from store.delete_item_internal(p_department_id, nullif(p_payload->>'item_id', '')::bigint);
                        when 'reorder' then
                            perform 1 from store.reorder_items_internal(p_department_id, coalesce(p_payload->'items', '[]'::jsonb));
                        else raise exception 'invalid_management_master_action';
                    end case;
                when 'nominations' then
                    if v_action <> 'save' then raise exception 'invalid_management_master_action'; end if;
                    perform 1 from store.save_nomination_back_master_internal(p_department_id, coalesce(p_payload->'settings', '[]'::jsonb));
                when 'pricing' then
                    if v_action <> 'save' then raise exception 'invalid_management_master_action'; end if;
                    perform 1 from store.save_pricing_plan_internal(
                        p_department_id,
                        nullif(p_payload->>'set_minutes', '')::integer,
                        nullif(p_payload->>'extension_minutes', '')::integer,
                        nullif(p_payload->>'set_unit_price_single', '')::numeric,
                        nullif(p_payload->>'set_unit_price_per_customer', '')::numeric,
                        nullif(p_payload->>'extension_unit_price_single', '')::numeric,
                        nullif(p_payload->>'extension_unit_price_per_customer', '')::numeric,
                        coalesce((p_payload->>'is_active')::boolean, false));
            end case;
        exception when others then
            v_status := 'validation_error';
            v_error_code := sqlstate;
            v_error_message := sqlerrm;
        end;
    end if;

    v_state := store.get_management_master_state(p_department_id);
    v_current_area_revision := v_state->'revisions'->>v_area;
    v_delta := v_state->v_area_key;
    v_response := jsonb_build_object(
        'status', v_status,
        'master_revision', md5((v_state->'revisions')::text),
        'area_revision', v_current_area_revision,
        'area', v_area,
        'master_delta', v_delta,
        'error_code', v_error_code,
        'error_message', v_error_message
    );

    insert into store.management_master_operation_results (
        department_id,
        operation_id,
        request_body,
        response
    ) values (
        p_department_id,
        v_operation_id,
        v_request_body,
        v_response
    );

    return query select
        v_status,
        v_response->>'master_revision',
        v_current_area_revision,
        v_area,
        v_delta,
        v_error_code,
        v_error_message;
end;
$$;

revoke all on function store.save_management_master_v2(bigint, text, text, text, text, jsonb, boolean)
    from public, anon, authenticated, service_role;

commit;
