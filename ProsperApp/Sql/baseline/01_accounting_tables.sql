-- accountingスキーマのbaseline定義。
--
-- ProsperAppは締め処理から仕訳を起票するため、これらのテーブルを参照します。
-- 会計側で先に作られていたテーブルなので、これまでリポジトリのSQLには定義が
-- ありませんでした。空のPostgreSQLからテスト用DBを作れるようにするための最小集合で、
-- ProsperAppが触らないOCR・証憑ワークアイテム系テーブルは意図的に含めません。
--
-- 本番へ流しても create table if not exists が全て no-op になります。

begin;

create schema if not exists accounting;

create table if not exists accounting.account_master (
    account_code text primary key,
    account_name text not null,
    category text not null,
    dc_type text not null,
    requires_subaccount boolean not null default false,
    is_active boolean not null default true,
    sort_order integer,
    search_key text,
    constraint account_master_dc_type_check
        check (dc_type = any (array['D', 'C', 'B'])),
    constraint account_master_search_key_format_check
        check (search_key is null or search_key ~ '^[A-Za-z]+$')
);

create unique index if not exists account_master_search_key_unique_idx
    on accounting.account_master (lower(search_key))
    where search_key is not null;

create table if not exists accounting.subaccount_master (
    subaccount_id bigint generated always as identity primary key,
    company_id bigint not null references public.company_master(company_id),
    subaccount_code text not null,
    subaccount_name text not null,
    subaccount_type text,
    is_active boolean not null default true,
    sort_order integer,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone,
    constraint subaccount_master_company_id_subaccount_code_key
        unique (company_id, subaccount_code)
);

-- upload_source_master の複合外部キーが参照するため、単独では冗長に見えても必要です。
create unique index if not exists subaccount_master_company_id_id_key
    on accounting.subaccount_master (company_id, subaccount_id);

create index if not exists idx_subaccount_master_company
    on accounting.subaccount_master (company_id, is_active, sort_order, subaccount_code);

create table if not exists accounting.account_subaccount_map (
    company_id bigint not null references public.company_master(company_id),
    account_code text not null references accounting.account_master(account_code),
    subaccount_id bigint not null references accounting.subaccount_master(subaccount_id),
    is_default boolean not null default false,
    sort_order integer,
    created_at timestamp with time zone not null default now(),
    primary key (company_id, account_code, subaccount_id)
);

create index if not exists account_subaccount_map_account_code_idx
    on accounting.account_subaccount_map (account_code);

create index if not exists account_subaccount_map_subaccount_id_idx
    on accounting.account_subaccount_map (subaccount_id);

create index if not exists idx_account_subaccount_map_company_account
    on accounting.account_subaccount_map (company_id, account_code, sort_order, subaccount_id);

create table if not exists accounting.upload_source_master (
    upload_source_id bigint generated always as identity primary key,
    company_id bigint not null references public.company_master(company_id),
    upload_source_code text not null,
    upload_source_name text not null,
    source_type text not null,
    default_department_id bigint references public.department_master(department_id),
    drive_source_folder_id text unique,
    is_active boolean not null default true,
    default_offset_account_code text references accounting.account_master(account_code),
    default_offset_subaccount_id bigint,
    constraint upload_source_master_company_id_upload_source_code_key
        unique (company_id, upload_source_code),
    constraint upload_source_default_offset_subaccount_company_fk
        foreign key (company_id, default_offset_subaccount_id)
        references accounting.subaccount_master(company_id, subaccount_id)
);

create index if not exists upload_source_master_default_department_id_idx
    on accounting.upload_source_master (default_department_id);

create table if not exists accounting.documents (
    document_id text primary key,
    company_id bigint not null references public.company_master(company_id),
    upload_source_id bigint references accounting.upload_source_master(upload_source_id),
    file_no text not null,
    original_file_name text,
    file_ext text,
    storage_path text not null,
    file_hash text,
    uploaded_at timestamp with time zone not null default now(),
    uploaded_by text,
    drive_file_id text unique,
    drive_url text,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now(),
    deleted_at timestamp with time zone,
    constraint documents_company_id_file_no_key unique (company_id, file_no)
);

create unique index if not exists documents_drive_file_id_unique
    on accounting.documents (drive_file_id)
    where drive_file_id is not null;

create index if not exists documents_uploaded_at_idx
    on accounting.documents (uploaded_at desc)
    where deleted_at is null;

create index if not exists documents_upload_source_idx
    on accounting.documents (upload_source_id)
    where upload_source_id is not null and deleted_at is null;

create index if not exists documents_company_source_uploaded_active_idx
    on accounting.documents (company_id, upload_source_id, uploaded_at, document_id)
    where deleted_at is null;

create table if not exists accounting.journal_entries (
    journal_entry_id uuid primary key default gen_random_uuid(),
    entry_date date not null,
    created_at timestamp with time zone not null default now(),
    created_by text,
    approved_at timestamp with time zone,
    approved_by text,
    status text not null default 'draft',
    updated_at timestamp with time zone not null default now(),
    description text,
    constraint journal_entries_status_check
        check (status = any (array['draft', 'confirmed', 'exported', 'voided']))
);

create table if not exists accounting.journal_entry_lines (
    line_id bigint generated always as identity primary key,
    journal_entry_id uuid not null
        references accounting.journal_entries(journal_entry_id) on delete cascade,
    line_no integer not null,
    account_code text not null references accounting.account_master(account_code),
    subaccount_id bigint references accounting.subaccount_master(subaccount_id),
    dc_flag text not null,
    amount numeric(12, 2) not null,
    line_memo text,
    account_name_snapshot text not null,
    subaccount_name_snapshot text,
    created_at timestamp with time zone not null default now(),
    department_id bigint references public.department_master(department_id),
    company_id bigint not null references public.company_master(company_id),
    invoice_category_code text,
    is_reduced_tax_rate boolean not null default false,
    constraint journal_entry_lines_amount_check check (amount >= 0),
    constraint journal_entry_lines_dc_flag_check check (dc_flag = any (array['D', 'C'])),
    constraint journal_entry_lines_journal_entry_id_line_no_key
        unique (journal_entry_id, line_no)
);

create index if not exists idx_journal_entry_lines_entry
    on accounting.journal_entry_lines (journal_entry_id, line_no);

create index if not exists journal_entry_lines_account_code_idx
    on accounting.journal_entry_lines (account_code);

create index if not exists journal_entry_lines_company_id_idx
    on accounting.journal_entry_lines (company_id);

create index if not exists journal_entry_lines_department_id_idx
    on accounting.journal_entry_lines (department_id);

create index if not exists journal_entry_lines_subaccount_id_idx
    on accounting.journal_entry_lines (subaccount_id);

create table if not exists accounting.document_journal_links (
    link_id bigint generated by default as identity primary key,
    document_id text not null
        references accounting.documents(document_id) on delete cascade,
    journal_entry_id uuid not null
        references accounting.journal_entries(journal_entry_id) on delete cascade,
    created_at timestamp with time zone not null default now(),
    constraint document_journal_links_document_id_journal_entry_id_key
        unique (document_id, journal_entry_id)
);

create index if not exists document_journal_links_journal_entry_idx
    on accounting.document_journal_links (journal_entry_id);

alter table accounting.account_master enable row level security;
alter table accounting.subaccount_master enable row level security;
alter table accounting.account_subaccount_map enable row level security;
alter table accounting.upload_source_master enable row level security;
alter table accounting.documents enable row level security;
alter table accounting.journal_entries enable row level security;
alter table accounting.journal_entry_lines enable row level security;
alter table accounting.document_journal_links enable row level security;

commit;
