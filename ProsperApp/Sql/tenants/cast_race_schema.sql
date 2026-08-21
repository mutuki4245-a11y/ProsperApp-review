-- CastRaceApp の cast_race スキーマ。
--
-- ProsperAppのスキーマではありません。同じSupabaseプロジェクトに同居している
-- 別アプリの定義で、apply_all.sh の対象にも含めていません。
--
-- ここに置いてある理由は、CastRaceApp にリポジトリが無いからです。この定義を
-- 消すと、スキーマの正本が本番DBの中だけになります。
--
-- 2026-08-21 に cast_race_newbee プロジェクトの public スキーマから移管しました。
-- 冪等なので再実行しても既存を壊しません。

create schema if not exists cast_race;
create extension if not exists pgcrypto;

create table if not exists cast_race.stores (
  id uuid primary key default gen_random_uuid(),
  name text not null unique,
  display_order integer not null default 0,
  is_active boolean not null default true,
  created_at timestamptz not null default now()
);

create table if not exists cast_race.casts (
  id uuid primary key default gen_random_uuid(),
  store_id uuid not null references cast_race.stores(id) on update cascade on delete restrict,
  name text not null,
  display_order integer not null default 0,
  is_active boolean not null default true,
  created_at timestamptz not null default now(),
  constraint casts_id_store_id_key unique (id, store_id),
  constraint casts_store_id_name_key unique (store_id, name)
);

create table if not exists cast_race.race_settings (
  id integer primary key default 1 check (id = 1),
  title text not null,
  start_date date not null,
  end_date date not null,
  updated_at timestamptz not null default now(),
  constraint race_settings_valid_date_range check (start_date <= end_date)
);

create table if not exists cast_race.sales_entries (
  id uuid primary key default gen_random_uuid(),
  business_date date not null,
  store_id uuid not null references cast_race.stores(id) on update cascade on delete restrict,
  cast_id uuid not null,
  amount integer not null default 0 check (amount >= 0),
  saved_at timestamptz not null default now(),
  constraint sales_entries_business_date_store_id_cast_id_key
    unique (business_date, store_id, cast_id),
  constraint sales_entries_cast_store_fk
    foreign key (cast_id, store_id) references cast_race.casts(id, store_id)
    on update cascade on delete restrict
);

create table if not exists cast_race.store_daily_saves (
  id uuid primary key default gen_random_uuid(),
  business_date date not null,
  store_id uuid not null references cast_race.stores(id) on update cascade on delete restrict,
  saved_at timestamptz not null default now(),
  constraint store_daily_saves_business_date_store_id_key unique (business_date, store_id)
);

create index if not exists idx_casts_store_order
  on cast_race.casts (store_id, is_active, display_order, name);
create index if not exists idx_sales_entries_cast_date
  on cast_race.sales_entries (cast_id, business_date);
create index if not exists idx_sales_entries_cast_store
  on cast_race.sales_entries (cast_id, store_id);
create index if not exists idx_sales_entries_store_date
  on cast_race.sales_entries (store_id, business_date);
create index if not exists idx_store_daily_saves_store_date
  on cast_race.store_daily_saves (store_id, business_date);

alter table cast_race.stores            enable row level security;
alter table cast_race.casts             enable row level security;
alter table cast_race.race_settings     enable row level security;
alter table cast_race.sales_entries     enable row level security;
alter table cast_race.store_daily_saves enable row level security;
