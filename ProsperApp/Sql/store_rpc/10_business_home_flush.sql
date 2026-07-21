-- 営業中トップの仮行を一度に確定するRPCです。
-- 通信再送では client_batch_id を同じ値のまま再利用し、追加操作を二重実行しません。

create table if not exists public.store_business_home_flush_batches (
    department_id bigint not null,
    business_day_id bigint not null,
    client_batch_id text not null,
    request_body jsonb not null,
    operation_results jsonb not null default '[]'::jsonb,
    karaoke_results jsonb not null default '[]'::jsonb,
    created_at timestamp with time zone not null default now(),
    primary key (department_id, business_day_id, client_batch_id)
);

alter table public.store_business_home_flush_batches enable row level security;
revoke all on table public.store_business_home_flush_batches from public, anon, authenticated, service_role;
create index if not exists store_business_home_flush_batches_created_at_idx
    on public.store_business_home_flush_batches (created_at);

drop function if exists store.flush_business_home_changes(bigint, bigint, text, jsonb, jsonb);

create or replace function store.flush_business_home_changes(
    p_department_id bigint,
    p_business_day_id bigint,
    p_client_batch_id text,
    p_operations jsonb default '[]'::jsonb,
    p_karaoke_lines jsonb default '[]'::jsonb
)
returns table (
    business_day_revision bigint,
    snapshot jsonb,
    operation_results jsonb,
    karaoke_results jsonb
)
language plpgsql
security definer
set search_path = public
as $$
declare
    v_request_body jsonb;
    v_existing_request_body jsonb;
    v_operation jsonb;
    v_karaoke_line jsonb;
    v_operation_id text;
    v_operation_type text;
    v_operation_slip_id bigint;
    v_karaoke_draft_id text;
    v_saved_count integer;
    v_error_message text;
    v_error_state text;
    v_revision bigint;
    v_snapshot jsonb;
    v_operation_results jsonb := '[]'::jsonb;
    v_karaoke_results jsonb := '[]'::jsonb;
    v_seen_operation_ids text[] := array[]::text[];
begin
    if p_department_id <= 0 or p_business_day_id <= 0 or
       nullif(trim(coalesce(p_client_batch_id, '')), '') is null or
       char_length(p_client_batch_id) > 100 then
        raise exception 'invalid_business_home_batch';
    end if;

    if jsonb_typeof(p_operations) <> 'array' or jsonb_typeof(p_karaoke_lines) <> 'array' or
       jsonb_array_length(p_operations) > 100 or jsonb_array_length(p_karaoke_lines) > 100 then
        raise exception 'invalid_business_home_batch';
    end if;

    v_request_body := jsonb_build_object(
        'operations', p_operations,
        'karaoke_lines', p_karaoke_lines
    );

    -- ブラウザ終了後は再送しないため、短期間の再送判定だけを残して古いバッチを掃除する。
    delete from public.store_business_home_flush_batches
    where created_at < now() - interval '7 days';

    -- 同じバッチだけを直列化する。別バッチは伝票ID昇順の行ロックで整列する。
    perform pg_advisory_xact_lock(hashtextextended(
        format('store_business_home_flush:%s:%s:%s', p_department_id, p_business_day_id, p_client_batch_id),
        0));

    select b.request_body, b.operation_results, b.karaoke_results
      into v_existing_request_body, v_operation_results, v_karaoke_results
    from public.store_business_home_flush_batches b
    where b.department_id = p_department_id
      and b.business_day_id = p_business_day_id
      and b.client_batch_id = p_client_batch_id;

    if found then
        if v_existing_request_body <> v_request_body then
            raise exception 'business_home_batch_id_reused';
        end if;
    else
        v_operation_results := '[]'::jsonb;
        v_karaoke_results := '[]'::jsonb;

        -- 同じバッチ内で複数伝票を触る場合も伝票ID順にロックする。
        perform 1
        from public.store_slips s
        where s.department_id = p_department_id
          and s.business_day_id = p_business_day_id
          and s.status = 'open'
          and s.slip_id in (
              select distinct (line.value->>'slip_id')::bigint
              from jsonb_array_elements(p_operations) line
              where jsonb_typeof(line.value) = 'object'
                and coalesce(line.value->>'slip_id', '') ~ '^[1-9][0-9]*$'
              union
              select distinct (line.value->>'slip_id')::bigint
              from jsonb_array_elements(p_karaoke_lines) line
              where jsonb_typeof(line.value) = 'object'
                and coalesce(line.value->>'slip_id', '') ~ '^[1-9][0-9]*$'
          )
        order by s.slip_id
        for update;

        for v_operation in
            select line.value
            from jsonb_array_elements(p_operations) with ordinality as line(value, ordinal)
            order by case
                when jsonb_typeof(line.value) = 'object' and coalesce(line.value->>'slip_id', '') ~ '^[1-9][0-9]*$'
                    then (line.value->>'slip_id')::bigint
                else 9223372036854775807
            end,
            line.ordinal
        loop
            v_operation_id := '';
            begin
                if jsonb_typeof(v_operation) <> 'object' then
                    raise exception 'invalid_business_editor_payload';
                end if;

                v_operation_id := nullif(trim(coalesce(v_operation->>'operation_id', '')), '');
                v_operation_type := nullif(trim(coalesce(v_operation->>'operation_type', '')), '');
                if v_operation_id is null or char_length(v_operation_id) > 100 or
                   v_operation_type is null or coalesce(v_operation->>'slip_id', '') !~ '^[1-9][0-9]*$' then
                    raise exception 'invalid_business_editor_operation';
                end if;

                if v_operation_id = any(v_seen_operation_ids) then
                    raise exception 'duplicate_business_editor_operation';
                end if;
                v_seen_operation_ids := array_append(v_seen_operation_ids, v_operation_id);
                v_operation_slip_id := (v_operation->>'slip_id')::bigint;

                perform store.apply_business_slip_editor_operation(
                    p_department_id,
                    p_business_day_id,
                    v_operation_slip_id,
                    v_operation_type,
                    v_operation_id,
                    coalesce(v_operation->'payload', '{}'::jsonb));

                v_operation_results := v_operation_results || jsonb_build_array(jsonb_build_object(
                    'operation_id', v_operation_id,
                    'succeeded', true
                ));
            exception when others then
                get stacked diagnostics v_error_message = message_text, v_error_state = returned_sqlstate;
                v_operation_results := v_operation_results || jsonb_build_array(jsonb_build_object(
                    'operation_id', coalesce(v_operation_id, ''),
                    'succeeded', false,
                    'message', coalesce(v_error_message, 'business_home_flush_failed'),
                    'sqlstate', coalesce(v_error_state, '')
                ));
            end;
        end loop;

        for v_karaoke_line in
            select line.value
            from jsonb_array_elements(p_karaoke_lines) with ordinality as line(value, ordinal)
            order by case
                when jsonb_typeof(line.value) = 'object' and coalesce(line.value->>'slip_id', '') ~ '^[1-9][0-9]*$'
                    then (line.value->>'slip_id')::bigint
                else 9223372036854775807
            end,
            line.ordinal
        loop
            v_karaoke_draft_id := '';
            begin
                if jsonb_typeof(v_karaoke_line) <> 'object' then
                    raise exception 'invalid_karaoke_quantity';
                end if;

                v_karaoke_draft_id := nullif(trim(coalesce(v_karaoke_line->>'draft_id', '')), '');
                if v_karaoke_draft_id is null or char_length(v_karaoke_draft_id) > 100 then
                    raise exception 'invalid_karaoke_quantity';
                end if;

                select saved_count
                  into v_saved_count
                from store.save_karaoke_lines(
                    p_department_id,
                    p_business_day_id,
                    jsonb_build_array(jsonb_build_object(
                        'slip_id', v_karaoke_line->>'slip_id',
                        'quantity', v_karaoke_line->>'quantity'
                    )));

                v_karaoke_results := v_karaoke_results || jsonb_build_array(jsonb_build_object(
                    'draft_id', v_karaoke_draft_id,
                    'slip_id', v_karaoke_line->>'slip_id',
                    'succeeded', true,
                    'saved_count', coalesce(v_saved_count, 0)
                ));
            exception when others then
                get stacked diagnostics v_error_message = message_text, v_error_state = returned_sqlstate;
                v_karaoke_results := v_karaoke_results || jsonb_build_array(jsonb_build_object(
                    'draft_id', coalesce(v_karaoke_draft_id, ''),
                    'slip_id', coalesce(v_karaoke_line->>'slip_id', ''),
                    'succeeded', false,
                    'message', coalesce(v_error_message, 'business_home_flush_failed'),
                    'sqlstate', coalesce(v_error_state, '')
                ));
            end;
        end loop;

        insert into public.store_business_home_flush_batches (
            department_id,
            business_day_id,
            client_batch_id,
            request_body,
            operation_results,
            karaoke_results
        )
        values (
            p_department_id,
            p_business_day_id,
            p_client_batch_id,
            v_request_body,
            v_operation_results,
            v_karaoke_results
        );
    end if;

    select s.business_day_revision, s.snapshot
      into v_revision, v_snapshot
    from store.get_business_day_snapshot(p_department_id, p_business_day_id) s;

    return query select v_revision, v_snapshot, v_operation_results, v_karaoke_results;
end;
$$;
