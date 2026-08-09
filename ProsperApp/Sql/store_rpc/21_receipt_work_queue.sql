-- 領収書の初期読取と進行mutationを専用の小さな作業キューに分離します。
-- キュー操作は operation_id で冪等にし、ブラウザは常に先頭の1件だけを送信します。

begin;

create table if not exists store.receipt_work_queue_operations (
    department_id bigint not null,
    operation_id text not null,
    action text not null,
    document_id text not null,
    request_hash text not null,
    response jsonb not null,
    created_at timestamp with time zone not null default now(),
    primary key (department_id, operation_id)
);

alter table store.receipt_work_queue_operations
    add column if not exists request_hash text;
delete from store.receipt_work_queue_operations where request_hash is null;
alter table store.receipt_work_queue_operations
    alter column request_hash set not null;

revoke all on table store.receipt_work_queue_operations from public, anon, authenticated, service_role;
create index if not exists receipt_work_queue_operations_created_at_idx
    on store.receipt_work_queue_operations (created_at);

drop function if exists store.get_current_receipt_work_queue(bigint);
drop function if exists store.get_current_receipt_work_queue(bigint, text);

create or replace function store.get_current_receipt_work_queue(
    p_department_id bigint,
    p_resume_cursor text default null
)
returns table (
    queue_revision text,
    pending_count integer,
    business_day jsonb,
    work_item jsonb,
    buffer jsonb,
    resume_cursor text,
    advance_casts jsonb
)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_business_day public.store_business_days%rowtype;
    v_candidates jsonb := '[]'::jsonb;
    v_buffer jsonb := '[]'::jsonb;
    v_advance_casts jsonb := '[]'::jsonb;
    v_queue_revision text;
    v_pending_count integer := 0;
    v_resume_cursor text;
begin
    if p_department_id <= 0 or not exists (
        select 1
          from public.department_master department
         where department.department_id = p_department_id
           and department.is_active = true
    ) then
        raise exception 'store_department_not_found';
    end if;

    select current_business_day.*
      into v_business_day
      from store.get_current_business_day_internal(p_department_id) current_business_day;

    select count(*)::integer,
           md5(coalesce(string_agg(
               receipt_row.document_id || ':' || document.updated_at::text,
               '|' order by receipt_row.document_id), ''))
      into v_pending_count, v_queue_revision
      from store.get_pending_receipts_internal(p_department_id, 'unprocessed') receipt_row
      join accounting.documents document
        on document.document_id = receipt_row.document_id;

    select coalesce(jsonb_agg(candidate.payload order by candidate.document_id), '[]'::jsonb)
      into v_candidates
      from (
          select receipt_row.document_id,
                 to_jsonb(receipt_row) || jsonb_build_object(
                     'work_item_token', md5(receipt_row.document_id || ':' || document.updated_at::text)
                 ) as payload
            from store.get_pending_receipts_internal(p_department_id, 'unprocessed') receipt_row
            join accounting.documents document
              on document.document_id = receipt_row.document_id
           where nullif(p_resume_cursor, '') is null
              or receipt_row.document_id > p_resume_cursor
           order by receipt_row.document_id
           limit 6
      ) candidate;

    select coalesce(jsonb_agg(candidate.value order by candidate.ordinal), '[]'::jsonb)
      into v_buffer
      from jsonb_array_elements(v_candidates) with ordinality candidate(value, ordinal)
     where candidate.ordinal > 1;

    if jsonb_array_length(v_candidates) = 6 then
        v_resume_cursor := v_candidates->5->>'document_id';
    end if;

    if v_business_day.business_day_id is not null then
        select coalesce(jsonb_agg(to_jsonb(attendance_row) order by attendance_row.display_name, attendance_row.attendance_id), '[]'::jsonb)
          into v_advance_casts
          from store.get_business_day_closing_attendance_internal(
              p_department_id,
              v_business_day.business_day_id) attendance_row
         where attendance_row.person_type = 'cast'
           and attendance_row.attendance_status in ('scheduled', 'checked_in', 'checked_out');
    end if;

    return query
    select
        v_queue_revision,
        v_pending_count,
        case when v_business_day.business_day_id is null then null else to_jsonb(v_business_day) end,
        case when jsonb_array_length(v_candidates) = 0 then null else v_candidates->0 end,
        v_buffer,
        v_resume_cursor,
        v_advance_casts;
end;
$$;

drop function if exists store.is_pending_receipt_drive_file_allowed(bigint, text);

create or replace function store.is_pending_receipt_drive_file_allowed(
    p_department_id bigint,
    p_drive_file_id text
)
returns table (
    is_allowed boolean
)
language sql
security definer
set search_path = pg_catalog, public
as $$
    select exists (
        select 1
          from store.get_pending_receipts_internal(p_department_id, 'unprocessed') receipt
         where receipt.drive_file_id = nullif(trim(p_drive_file_id), '')
    ) as is_allowed;
$$;

drop function if exists store.advance_receipt_work_queue_v2(bigint, text, text, text, text, date, numeric, text, text, text, bigint);

create or replace function store.advance_receipt_work_queue_v2(
    p_department_id bigint,
    p_operation_id text,
    p_action text,
    p_work_item_token text,
    p_document_id text,
    p_payment_date date default null,
    p_amount numeric default null,
    p_account_subject text default null,
    p_description text default null,
    p_group_code text default null,
    p_advance_cast_id bigint default null
)
returns table (response jsonb)
language plpgsql
security definer
set search_path = pg_catalog, public
as $$
declare
    v_operation_id text := lower(trim(coalesce(p_operation_id, '')));
    v_action text := lower(trim(coalesce(p_action, '')));
    v_document_id text := trim(coalesce(p_document_id, ''));
    v_existing_action text;
    v_existing_document_id text;
    v_existing_request_hash text;
    v_request_hash text;
    v_response jsonb;
    v_company_id bigint;
    v_business_day_id bigint;
    v_journal_payload jsonb;
    v_account_code text;
    v_memo text;
    v_work_item_token text;
    v_error_message text;
    v_error_state text;
    v_queue jsonb;
    v_current_business_day public.store_business_days%rowtype;
begin
    if p_department_id <= 0 or
       v_operation_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' or
       v_action not in ('save', 'exclude_scan_mistake') or
       v_document_id = '' then
        return query select jsonb_build_object(
            'status', 'validation_error',
            'message', '領収書の操作内容を確認してください。'
        );
        return;
    end if;

    v_request_hash := md5(jsonb_build_object(
        'action', v_action,
        'work_item_token', coalesce(p_work_item_token, ''),
        'document_id', v_document_id,
        'payment_date', p_payment_date,
        'amount', p_amount,
        'account_subject', nullif(trim(coalesce(p_account_subject, '')), ''),
        'description', nullif(trim(coalesce(p_description, '')), ''),
        'group_code', nullif(trim(coalesce(p_group_code, '')), ''),
        'advance_cast_id', p_advance_cast_id
    )::text);

    perform pg_advisory_xact_lock(hashtextextended(
        format('receipt_work_queue:%s:%s', p_department_id, v_document_id),
        0));

    delete from store.receipt_work_queue_operations
     where created_at < now() - interval '30 days';

    select operation.action, operation.document_id, operation.request_hash, operation.response
      into v_existing_action, v_existing_document_id, v_existing_request_hash, v_response
      from store.receipt_work_queue_operations operation
     where operation.department_id = p_department_id
       and operation.operation_id = v_operation_id;

    if found then
        if v_existing_action <> v_action or
           v_existing_document_id <> v_document_id or
           v_existing_request_hash <> v_request_hash then
            raise exception 'receipt_operation_id_reused';
        end if;

        return query select v_response;
        return;
    end if;

    select md5(document.document_id || ':' || document.updated_at::text)
      into v_work_item_token
      from accounting.documents document
     where document.document_id = v_document_id
       and document.deleted_at is null;

    if v_work_item_token is null or coalesce(p_work_item_token, '') <> v_work_item_token then
        v_response := jsonb_build_object(
            'status', 'stale_work_item',
            'document_id', v_document_id,
            'message', '表示していた証憑が更新されています。作業キューを再取得してください。'
        );
    else
        begin
            if v_action = 'save' then
                if p_payment_date is null or
                   p_payment_date < ((now() at time zone 'Asia/Tokyo')::date - interval '1 year') or
                   p_payment_date > (now() at time zone 'Asia/Tokyo')::date or
                   p_amount is null or p_amount <= 0 or p_amount > 9999999 or trunc(p_amount) <> p_amount or
                   nullif(trim(coalesce(p_account_subject, '')), '') is null or
                   char_length(trim(p_account_subject)) > 100 or
                   nullif(trim(coalesce(p_description, '')), '') is null or
                   char_length(trim(p_description)) > 500 or
                   char_length(trim(coalesce(p_group_code, ''))) > 32 then
                    raise exception 'invalid_receipt_input';
                end if;

                select department.company_id
                  into v_company_id
                  from public.department_master department
                 where department.department_id = p_department_id
                   and department.is_active = true;

                if v_company_id is null then
                    raise exception 'store_department_not_found';
                end if;

                select current_business_day.*
                  into v_current_business_day
                  from store.get_current_business_day_internal(p_department_id) current_business_day;

                if v_current_business_day.business_day_id is null then
                    raise exception 'receipt_reimbursement_business_day_required';
                end if;

                v_business_day_id := v_current_business_day.business_day_id;

                v_account_code := trim(split_part(replace(p_account_subject, '：', ':'), ':', 1));
                v_memo := trim(p_description) || case
                    when nullif(trim(coalesce(p_group_code, '')), '') is null then ''
                    else ' [G:' || trim(p_group_code) || ']'
                end;
                v_journal_payload := jsonb_build_object(
                    'journal_entries', jsonb_build_array(jsonb_build_object(
                        'journal_entry_id', v_operation_id,
                        'journal_date', p_payment_date,
                        'status', 'confirmed'
                    )),
                    'journal_entry_lines', jsonb_build_array(
                        jsonb_build_object(
                            'journal_entry_id', v_operation_id,
                            'line_no', 1,
                            'side', 'debit',
                            'account_code', v_account_code,
                            'company_id', v_company_id,
                            'department_id', p_department_id,
                            'is_reduced_tax_rate', false,
                            'line_memo', v_memo,
                            'amount', p_amount
                        ),
                        jsonb_build_object(
                            'journal_entry_id', v_operation_id,
                            'line_no', 2,
                            'side', 'credit',
                            'account_code', '現金',
                            'company_id', v_company_id,
                            'department_id', null,
                            'is_reduced_tax_rate', false,
                            'line_memo', v_memo,
                            'amount', p_amount
                        )
                    ),
                    'document_journal_links', jsonb_build_array(jsonb_build_object(
                        'journal_entry_id', v_operation_id,
                        'document_id', v_document_id
                    ))
                );

                perform 1
                  from store.quick_enter_receipt_internal(
                      p_department_id,
                      v_document_id,
                      p_payment_date,
                      p_amount,
                      trim(p_account_subject),
                      trim(p_description),
                      nullif(trim(coalesce(p_group_code, '')), ''),
                      v_journal_payload,
                      'quick_entered',
                      v_business_day_id,
                      p_advance_cast_id);
                if not found then
                    raise exception 'pending_receipt_not_found';
                end if;
            else
                perform 1
                  from store.mark_receipt_scan_mistake_internal(
                      p_department_id,
                      v_document_id,
                      'excluded');
                if not found then
                    raise exception 'receipt_scan_mistake_not_found';
                end if;
            end if;

            v_response := jsonb_build_object(
                'status', 'confirmed',
                'document_id', v_document_id
            );
        exception when others then
            get stacked diagnostics v_error_message = message_text, v_error_state = returned_sqlstate;
            if v_error_message in (
                'pending_receipt_not_found',
                'receipt_scan_mistake_not_found'
            ) then
                v_response := jsonb_build_object(
                    'status', 'stale_work_item',
                    'document_id', v_document_id,
                    'message', '別の端末で処理済みか対象外になっています。'
                );
            elsif v_error_message like 'invalid_%' or
                  v_error_message like 'cast_advance_%' or
                  v_error_message in (
                      'receipt_amount_must_be_integer',
                      'unexpected_cast_advance_selection'
                  ) then
                v_response := jsonb_build_object(
                    'status', 'validation_error',
                    'document_id', v_document_id,
                    'message', v_error_message
                );
            elsif v_error_message like 'receipt_reimbursement_%' then
                v_response := jsonb_build_object(
                    'status', 'validation_error',
                    'document_id', v_document_id,
                    'message', '営業日を開始してから領収書を入力してください。'
                );
            else
                raise;
            end if;
        end;
    end if;

    select to_jsonb(queue_state)
      into v_queue
      from store.get_current_receipt_work_queue(p_department_id, null) queue_state;

    v_response := v_response || jsonb_build_object(
        'queue_revision', v_queue->>'queue_revision',
        'pending_count', coalesce(nullif(v_queue->>'pending_count', '')::integer, 0),
        'business_day', v_queue->'business_day',
        'work_item', v_queue->'work_item',
        'buffer', coalesce(v_queue->'buffer', '[]'::jsonb),
        'resume_cursor', v_queue->>'resume_cursor',
        'advance_casts', coalesce(v_queue->'advance_casts', '[]'::jsonb),
        'pending_receipt_count', coalesce(nullif(v_queue->>'pending_count', '')::integer, 0)
    );

    insert into store.receipt_work_queue_operations (
        department_id,
        operation_id,
        action,
        document_id,
        request_hash,
        response
    ) values (
        p_department_id,
        v_operation_id,
        v_action,
        v_document_id,
        v_request_hash,
        v_response
    );

    return query select v_response;
end;
$$;

revoke all on function store.get_current_receipt_work_queue(bigint, text)
    from public, anon, authenticated, service_role;
revoke all on function store.is_pending_receipt_drive_file_allowed(bigint, text)
    from public, anon, authenticated, service_role;
revoke all on function store.advance_receipt_work_queue_v2(bigint, text, text, text, text, date, numeric, text, text, text, bigint)
    from public, anon, authenticated, service_role;

commit;
