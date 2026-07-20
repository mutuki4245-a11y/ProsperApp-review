begin;

-- Missing or renamed account subjects used by the tablet quick-entry UI.
insert into accounting.account_master (
    account_code,
    account_name,
    category,
    dc_type,
    requires_subaccount,
    is_active,
    sort_order
)
select '5290', '保険料', 'expense', 'D', true, true, 690
where not exists (
    select 1 from accounting.account_master where account_code = '5290'
);

update accounting.account_master
set account_name = 'リース料',
    requires_subaccount = true
where account_code = '5210'
  and account_name <> 'リース料';

update accounting.account_master
set account_name = '衛生費',
    requires_subaccount = true
where account_code = '5250'
  and account_name <> '衛生費';

-- Company-specific subaccounts for quick-entry choices.
with companies as (
    select company_id from public.company_master where is_active = true
),
subaccounts as (
    select * from (values
        ('1160', 'SUB015', 'スタッフ前渡し', 150),
        ('5110', 'SUB016', 'スタッフ日払い', 160),
        ('5140', 'SUB017', 'ヘアメイク', 170),
        ('5190', 'SUB018', '水道', 180),
        ('5190', 'SUB019', '電気', 190),
        ('5190', 'SUB020', 'ガス', 200),
        ('5170', 'SUB021', '電話', 210),
        ('5170', 'SUB022', 'ネット', 220),
        ('5210', 'SUB023', 'カラオケ', 230),
        ('5210', 'SUB024', 'おしぼり', 240),
        ('5210', 'SUB025', 'ダーツマシン', 250),
        ('5250', 'SUB026', 'ごみ処理', 260),
        ('5250', 'SUB027', 'ダスキン', 270),
        ('5250', 'SUB028', '害虫駆除', 280),
        ('5150', 'SUB029', '名刺/看板/ポスター', 290),
        ('5150', 'SUB030', '募集', 300),
        ('5150', 'SUB031', '案内所', 310),
        ('5010', 'SUB032', '食材', 320),
        ('5010', 'SUB033', '酒', 330),
        ('5010', 'SUB034', '出前', 340),
        ('5180', 'SUB035', '事務用品', 350),
        ('5180', 'SUB036', '衛生用品', 360),
        ('5290', 'SUB037', '自動車保険', 370),
        ('5290', 'SUB038', '店舗火災保険', 380),
        ('5160', 'SUB039', 'タクシー/電車代', 390),
        ('5160', 'SUB040', 'ガソリン代', 400),
        ('5200', 'SUB041', '駐車場', 410),
        ('5200', 'SUB042', '店舗家賃', 420),
        ('5130', 'SUB043', '飲食費', 430),
        ('5240', 'SUB044', 'ミーティング', 440),
        ('5230', 'SUB045', 'キャスト飲食費', 450),
        ('5250', 'SUB046', 'コピー機', 460),
        ('5250', 'SUB047', 'クリーニング', 470),
        ('5210', 'SUB048', 'JASRAC著作権利用料', 480)
    ) as v(account_code, subaccount_code, subaccount_name, sort_order)
),
inserted_subaccounts as (
    insert into accounting.subaccount_master (
        company_id,
        subaccount_code,
        subaccount_name,
        subaccount_type,
        is_active,
        sort_order
    )
    select
        c.company_id,
        s.subaccount_code,
        s.subaccount_name,
        'quick_entry',
        true,
        s.sort_order
    from companies c
    cross join subaccounts s
    where not exists (
        select 1
        from accounting.subaccount_master existing
        where existing.company_id = c.company_id
          and existing.subaccount_code = s.subaccount_code
    )
    returning company_id, subaccount_id, subaccount_code
)
insert into accounting.account_subaccount_map (
    company_id,
    account_code,
    subaccount_id,
    is_default,
    sort_order,
    created_at
)
select
    sm.company_id,
    s.account_code,
    sm.subaccount_id,
    false,
    s.sort_order,
    now()
from subaccounts s
join accounting.subaccount_master sm
  on sm.subaccount_code = s.subaccount_code
join companies c
  on c.company_id = sm.company_id
where not exists (
    select 1
    from accounting.account_subaccount_map existing
    where existing.company_id = sm.company_id
      and existing.account_code = s.account_code
      and existing.subaccount_id = sm.subaccount_id
);

commit;
