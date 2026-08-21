-- mieu本店 商品マスタ投入
-- Source: product_list_final (1).csv
-- Source SHA-256: EE0985194689FEB0AD841DC10EC860883124CBAC09133B7CFD568B1911B6E4FE
-- CSV rows: 52
--
-- 方針:
-- - department_id = 1 / mieu本店だけを対象にする。
-- - 既存の「1000」は、商品IDを維持したまま「ドリンク1000」へ改名する。
-- - 既存カテゴリ（ドリンク、フード、シャンパン）はそのまま使う。
-- - ボトルカテゴリだけを追加する。
-- - 同一商品名で再実行した場合は、CSVの価格・カテゴリ・バック設定へ更新する。
-- - システム商品には触れない。
--
-- 戻し方:
-- - この投入を直後に取り消す場合は mieu_honten_product_master_seed_rollback.sql を実行する。
-- - 注文運用開始後のロールバックは、商品マスタから商品が消えるため事前に影響を確認する。

begin;

do $$
declare
    v_company_id bigint;
    v_old_drink_count integer;
    v_new_drink_count integer;
begin
    select d.company_id
      into v_company_id
    from public.department_master d
    where d.department_id = 1
      and d.department_name = 'mieu本店'
      and d.is_active = true;

    if v_company_id is null then
        raise exception 'target_department_not_found:mieu本店';
    end if;

    if not exists (
        select 1
        from public.store_item_category_master c
        where c.company_id = v_company_id
          and c.department_id = 1
          and c.category_code = 'drink'
          and c.category_name = 'ドリンク'
          and c.is_active = true
    ) then
        raise exception 'required_category_not_found:drink';
    end if;

    if not exists (
        select 1
        from public.store_item_category_master c
        where c.company_id = v_company_id
          and c.department_id = 1
          and c.category_code = 'food'
          and c.category_name = 'フード'
          and c.is_active = true
    ) then
        raise exception 'required_category_not_found:food';
    end if;

    if not exists (
        select 1
        from public.store_item_category_master c
        where c.company_id = v_company_id
          and c.department_id = 1
          and c.category_code = 'Champagne'
          and c.category_name = 'シャンパン'
          and c.is_active = true
    ) then
        raise exception 'required_category_not_found:Champagne';
    end if;

    select count(*)::integer
      into v_old_drink_count
    from public.store_item_master i
    where i.company_id = v_company_id
      and i.department_id = 1
      and i.item_name = '1000';

    select count(*)::integer
      into v_new_drink_count
    from public.store_item_master i
    where i.company_id = v_company_id
      and i.department_id = 1
      and i.item_name = 'ドリンク1000';

    if v_old_drink_count + v_new_drink_count <> 1 then
        raise exception
            'unexpected_drink1000_state:old=%,new=%',
            v_old_drink_count,
            v_new_drink_count;
    end if;

    if v_old_drink_count = 1 and not exists (
        select 1
        from public.store_item_master i
        join public.store_item_category_master c
          on c.item_category_id = i.item_category_id
         and c.company_id = i.company_id
         and c.department_id = i.department_id
        where i.company_id = v_company_id
          and i.department_id = 1
          and i.item_name = '1000'
          and i.item_type = 'standard'
          and i.default_price = 1000
          and i.is_active = true
          and i.is_cast_back_target = true
          and i.cast_back_regular_unit_amount = 200
          and i.cast_back_nomination_unit_amount = 300
          and i.cast_back_type = 'drink'
          and c.category_code = 'drink'
    ) then
        raise exception 'existing_1000_does_not_match_expected_values';
    end if;
end;
$$;

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
    'bottle',
    'ボトル',
    40,
    true
from public.department_master d
where d.department_id = 1
  and d.department_name = 'mieu本店'
  and d.is_active = true
on conflict (company_id, department_id, category_code)
do update set
    category_name = excluded.category_name,
    sort_order = excluded.sort_order,
    is_active = true,
    updated_at = now();

update public.store_item_master i
   set item_name = 'ドリンク1000',
       updated_at = now()
  from public.department_master d
 where d.department_id = 1
   and d.department_name = 'mieu本店'
   and d.is_active = true
   and i.company_id = d.company_id
   and i.department_id = d.department_id
   and i.item_name = '1000'
   and not exists (
       select 1
       from public.store_item_master existing_item
       where existing_item.company_id = i.company_id
         and existing_item.department_id = i.department_id
         and existing_item.item_name = 'ドリンク1000'
   );

create temporary table prosper_mieu_product_seed (
    source_row integer primary key,
    category_code text not null,
    item_name text not null unique,
    default_price numeric(12, 0) not null,
    is_cast_back_target boolean not null,
    cast_back_regular_unit_amount numeric(12, 0) not null,
    cast_back_nomination_unit_amount numeric(12, 0) not null,
    sort_order integer not null
) on commit drop;

insert into prosper_mieu_product_seed (
    source_row,
    category_code,
    item_name,
    default_price,
    is_cast_back_target,
    cast_back_regular_unit_amount,
    cast_back_nomination_unit_amount,
    sort_order
)
values
    (1,  'bottle',    'CAMUS VSOP',                 9000, false,   0,   0,  10),
    (2,  'bottle',    'Hennessy VSOP',             30000, false,   0,   0,  20),
    (3,  'bottle',    'Hennessy XO',              100000, false,   0,   0,  30),
    (4,  'Champagne', 'VOGA（赤）',                10000, false,   0,   0,  10),
    (5,  'Champagne', 'CARNIVOR（赤）',            10000, false,   0,   0,  20),
    (6,  'Champagne', 'MOMA（白）',                10000, false,   0,   0,  30),
    (7,  'Champagne', 'ヴーヴイエロー',             30000, false,   0,   0,  40),
    (8,  'Champagne', 'ヴーヴエクストラ',           50000, false,   0,   0,  50),
    (9,  'Champagne', 'ヴーヴグランダム',          100000, false,   0,   0,  60),
    (10, 'Champagne', 'ローランペリエブリュット',     25000, false,   0,   0,  70),
    (11, 'Champagne', 'ローランペリエロゼ',           60000, false,   0,   0,  80),
    (12, 'Champagne', 'ローランペリエウルトラ',        70000, false,   0,   0,  90),
    (13, 'Champagne', 'モエネクター',                35000, false,   0,   0, 100),
    (14, 'Champagne', 'モエアイス',                  40000, false,   0,   0, 110),
    (15, 'Champagne', 'モエアイスロゼ',               45000, false,   0,   0, 120),
    (16, 'Champagne', 'モエN.I.R',                  50000, false,   0,   0, 130),
    (17, 'Champagne', 'モエルミナス',                60000, false,   0,   0, 140),
    (18, 'Champagne', 'ベルエポックロゼ',            180000, false,   0,   0, 150),
    (19, 'Champagne', 'ソウメイブリュット',          120000, false,   0,   0, 160),
    (20, 'Champagne', 'ソウメイロゼ',               200000, false,   0,   0, 170),
    (21, 'Champagne', 'アルマンドゴールド',          160000, false,   0,   0, 180),
    (22, 'Champagne', 'エンジェルブラック',          200000, false,   0,   0, 190),
    (23, 'Champagne', 'エンジェルホワイト',          250000, false,   0,   0, 200),
    (24, 'Champagne', 'エンジェルピンク／ブルー',      350000, false,   0,   0, 210),
    (25, 'drink',     'ドリンク1000',                 1000, true,  200, 300,  10),
    (26, 'drink',     'ドリンク1500',                 1500, true,  200, 300,  20),
    (27, 'drink',     'ドリンク2000',                 2000, true,  200, 300,  30),
    (28, 'drink',     'ドリンク3000',                 3000, true,  200, 300,  40),
    (29, 'food',      '枝豆',                         1000, false,   0,   0,  10),
    (30, 'food',      '砂肝',                         1000, false,   0,   0,  20),
    (31, 'food',      'ナゲット',                      1000, false,   0,   0,  30),
    (32, 'food',      'たこ焼き',                      1000, false,   0,   0,  40),
    (33, 'food',      '焼きそば',                      1000, false,   0,   0,  50),
    (34, 'food',      'チャーハン',                     1500, false,   0,   0,  60),
    (35, 'bottle',    'ふんわり鏡月',                   3000, false,   0,   0,  40),
    (36, 'bottle',    'CHOYA梅',                      5000, false,   0,   0,  50),
    (37, 'bottle',    'れんと',                        6000, false,   0,   0,  60),
    (38, 'bottle',    '鍛高譚',                        6000, false,   0,   0,  70),
    (39, 'bottle',    '鍛高譚梅',                      7000, false,   0,   0,  80),
    (40, 'bottle',    '茜霧島',                        8000, false,   0,   0,  90),
    (41, 'bottle',    '赤霧島',                        8000, false,   0,   0, 100),
    (42, 'bottle',    '鳥飼',                         8000, false,   0,   0, 110),
    (43, 'bottle',    '吉四六',                       10000, false,   0,   0, 120),
    (44, 'bottle',    'Four Roses',                  7000, false,   0,   0, 130),
    (45, 'bottle',    'CHIVAS REGAL',                7000, false,   0,   0, 140),
    (46, 'bottle',    'I.W. HARPER',                 8000, false,   0,   0, 150),
    (47, 'bottle',    'Maker''s Mark',              12000, false,   0,   0, 160),
    (48, 'bottle',    'CHIVAS REGAL MIZUNARA',      12000, false,   0,   0, 170),
    (49, 'bottle',    '知多',                        30000, false,   0,   0, 180),
    (50, 'bottle',    '山崎',                        30000, false,   0,   0, 190),
    (51, 'bottle',    '白州',                        30000, false,   0,   0, 200),
    (52, 'bottle',    'MACALLAN 12',                50000, false,   0,   0, 210);

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
select
    d.company_id,
    d.department_id,
    c.item_category_id,
    s.item_name,
    'standard',
    s.default_price,
    s.is_cast_back_target,
    s.cast_back_regular_unit_amount,
    s.cast_back_regular_unit_amount,
    s.cast_back_nomination_unit_amount,
    'drink',
    s.sort_order,
    true
from prosper_mieu_product_seed s
join public.department_master d
  on d.department_id = 1
 and d.department_name = 'mieu本店'
 and d.is_active = true
join public.store_item_category_master c
  on c.company_id = d.company_id
 and c.department_id = d.department_id
 and c.category_code = s.category_code
 and c.is_active = true
on conflict (company_id, department_id, item_name)
do update set
    item_category_id = excluded.item_category_id,
    item_type = excluded.item_type,
    default_price = excluded.default_price,
    is_cast_back_target = excluded.is_cast_back_target,
    cast_back_unit_amount = excluded.cast_back_unit_amount,
    cast_back_regular_unit_amount = excluded.cast_back_regular_unit_amount,
    cast_back_nomination_unit_amount = excluded.cast_back_nomination_unit_amount,
    cast_back_type = excluded.cast_back_type,
    sort_order = excluded.sort_order,
    is_active = excluded.is_active,
    updated_at = now();

do $$
declare
    v_seed_count integer;
    v_matched_count integer;
    v_mismatch_count integer;
    v_standard_count integer;
begin
    select count(*)::integer
      into v_seed_count
    from prosper_mieu_product_seed;

    if v_seed_count <> 52 then
        raise exception 'invalid_seed_count:%', v_seed_count;
    end if;

    select count(*)::integer
      into v_matched_count
    from prosper_mieu_product_seed s
    join public.department_master d
      on d.department_id = 1
     and d.department_name = 'mieu本店'
     and d.is_active = true
    join public.store_item_master i
      on i.company_id = d.company_id
     and i.department_id = d.department_id
     and i.item_name = s.item_name;

    if v_matched_count <> 52 then
        raise exception 'seeded_item_count_mismatch:%', v_matched_count;
    end if;

    select count(*)::integer
      into v_mismatch_count
    from prosper_mieu_product_seed s
    join public.department_master d
      on d.department_id = 1
     and d.department_name = 'mieu本店'
     and d.is_active = true
    join public.store_item_category_master c
      on c.company_id = d.company_id
     and c.department_id = d.department_id
     and c.category_code = s.category_code
    join public.store_item_master i
      on i.company_id = d.company_id
     and i.department_id = d.department_id
     and i.item_name = s.item_name
    where i.item_category_id is distinct from c.item_category_id
       or i.item_type is distinct from 'standard'
       or i.default_price is distinct from s.default_price
       or i.is_active is distinct from true
       or i.is_cast_back_target is distinct from s.is_cast_back_target
       or i.cast_back_unit_amount is distinct from s.cast_back_regular_unit_amount
       or i.cast_back_regular_unit_amount is distinct from s.cast_back_regular_unit_amount
       or i.cast_back_nomination_unit_amount is distinct from s.cast_back_nomination_unit_amount
       or i.cast_back_type is distinct from 'drink'
       or i.sort_order is distinct from s.sort_order;

    if v_mismatch_count <> 0 then
        raise exception 'seeded_item_value_mismatch:%', v_mismatch_count;
    end if;

    if exists (
        select 1
        from public.store_item_master i
        where i.department_id = 1
          and i.item_name = '1000'
    ) then
        raise exception 'old_item_name_still_exists:1000';
    end if;

    select count(*)::integer
      into v_standard_count
    from public.store_item_master i
    where i.department_id = 1
      and i.item_type = 'standard';

    if v_standard_count <> 52 then
        raise exception 'unexpected_standard_item_count:%', v_standard_count;
    end if;
end;
$$;

select
    d.department_id,
    d.department_name,
    (
        select count(*)
        from public.store_item_master i
        where i.company_id = d.company_id
          and i.department_id = d.department_id
          and i.item_type = 'standard'
    ) as standard_item_count,
    (
        select count(*)
        from public.store_item_master i
        where i.company_id = d.company_id
          and i.department_id = d.department_id
          and i.item_type <> 'standard'
    ) as system_item_count,
    (
        select count(*)
        from public.store_item_master i
        where i.company_id = d.company_id
          and i.department_id = d.department_id
          and i.item_type = 'standard'
          and i.is_cast_back_target = true
    ) as back_target_item_count
from public.department_master d
where d.department_id = 1
  and d.department_name = 'mieu本店';

commit;
