begin;

create table if not exists store.app_users (
  app_user_id bigint generated always as identity primary key,
  normalized_email text not null unique,
  google_subject text unique,
  is_active boolean not null default true,
  created_at timestamp with time zone not null default clock_timestamp(),
  updated_at timestamp with time zone not null default clock_timestamp(),
  last_login_at timestamp with time zone,
  constraint ck_app_users_normalized_email
    check (normalized_email = lower(btrim(normalized_email)) and normalized_email like '%@%'),
  constraint ck_app_users_google_subject
    check (google_subject is null or btrim(google_subject) <> '')
);

create table if not exists store.app_user_department_access (
  app_user_id bigint not null references store.app_users(app_user_id) on delete cascade,
  department_id bigint not null references public.department_master(department_id),
  access_role text not null,
  is_default boolean not null default false,
  created_at timestamp with time zone not null default clock_timestamp(),
  updated_at timestamp with time zone not null default clock_timestamp(),
  primary key (app_user_id, department_id),
  constraint ck_app_user_department_access_role
    check (access_role in ('operator', 'administrator'))
);

create unique index if not exists ux_app_user_department_access_default
  on store.app_user_department_access(app_user_id)
  where is_default;
create index if not exists ix_app_user_department_access_department
  on store.app_user_department_access(department_id);

revoke all on table store.app_users from public, anon, authenticated, service_role;
revoke all on table store.app_user_department_access from public, anon, authenticated, service_role;
revoke all on sequence store.app_users_app_user_id_seq from public, anon, authenticated, service_role;

create or replace function store.bind_app_user_access(
  p_email text,
  p_google_subject text,
  p_email_verified boolean
)
returns table (
  department_id bigint,
  access_role text,
  is_default boolean
)
language plpgsql
security definer
set search_path = pg_catalog, store, public
as $function$
declare
  v_email text := lower(btrim(coalesce(p_email, '')));
  v_subject text := btrim(coalesce(p_google_subject, ''));
  v_user store.app_users%rowtype;
begin
  if p_email_verified is not true or v_email = '' or v_subject = '' then
    raise exception using errcode = '28000', message = 'access_denied';
  end if;

  select *
    into v_user
    from store.app_users au
   where au.normalized_email = v_email
   for update;

  if not found or v_user.is_active is not true then
    raise exception using errcode = '28000', message = 'access_denied';
  end if;

  if v_user.google_subject is null then
    begin
      update store.app_users
         set google_subject = v_subject,
             updated_at = clock_timestamp(),
             last_login_at = clock_timestamp()
       where app_user_id = v_user.app_user_id;
    exception when unique_violation then
      raise exception using errcode = '28000', message = 'access_denied';
    end;
  elsif v_user.google_subject is distinct from v_subject then
    raise exception using errcode = '28000', message = 'access_denied';
  else
    update store.app_users
       set last_login_at = clock_timestamp()
     where app_user_id = v_user.app_user_id;
  end if;

  return query
  select a.department_id, a.access_role, a.is_default
    from store.app_user_department_access a
   where a.app_user_id = v_user.app_user_id
   order by a.is_default desc, a.department_id;

  if not found then
    raise exception using errcode = '28000', message = 'access_denied';
  end if;
end;
$function$;

create or replace function store.authorize_app_user_access(
  p_email text,
  p_google_subject text,
  p_department_id bigint,
  p_required_role text default 'operator'
)
returns boolean
language sql
stable
security definer
set search_path = pg_catalog, store, public
as $function$
  select exists (
    select 1
      from store.app_users au
      join store.app_user_department_access a using (app_user_id)
     where au.normalized_email = lower(btrim(coalesce(p_email, '')))
       and au.google_subject = btrim(coalesce(p_google_subject, ''))
       and au.is_active
       and a.department_id = p_department_id
       and (
         p_required_role = 'operator'
         or (p_required_role = 'administrator' and a.access_role = 'administrator')
       )
  );
$function$;

create or replace function store.get_app_user_access(
  p_email text,
  p_google_subject text
)
returns table (
  department_id bigint,
  access_role text,
  is_default boolean
)
language sql
stable
security definer
set search_path = pg_catalog, store, public
as $function$
  select a.department_id, a.access_role, a.is_default
    from store.app_users au
    join store.app_user_department_access a using (app_user_id)
   where au.normalized_email = lower(btrim(coalesce(p_email, '')))
     and au.google_subject = btrim(coalesce(p_google_subject, ''))
     and au.is_active
   order by a.is_default desc, a.department_id;
$function$;

revoke all on function store.bind_app_user_access(text, text, boolean)
  from public, anon, authenticated, service_role;
revoke all on function store.authorize_app_user_access(text, text, bigint, text)
  from public, anon, authenticated, service_role;
revoke all on function store.get_app_user_access(text, text)
  from public, anon, authenticated, service_role;

insert into store.app_users(normalized_email)
values
  ('mutuk4245@goat-takahashi.com'),
  ('goat1234.prosper@gmail.com')
on conflict (normalized_email) do update
set is_active = true,
    updated_at = clock_timestamp();

insert into store.app_user_department_access(app_user_id, department_id, access_role, is_default)
select au.app_user_id, 1, 'administrator', true
  from store.app_users au
 where au.normalized_email in (
   'mutuk4245@goat-takahashi.com',
   'goat1234.prosper@gmail.com'
 )
on conflict (app_user_id, department_id) do update
set access_role = excluded.access_role,
    is_default = excluded.is_default,
    updated_at = clock_timestamp();

commit;
