-- mieu_honten_product_master_seed.sql の直後ロールバック用。
-- 注文運用開始後は、対象商品をマスタから削除してよいか確認してから実行する。
-- 注文明細の商品名・価格スナップショットは残るが、商品選択肢からは消える。

begin;

do $$
begin
    if not exists (
        select 1
        from public.department_master d
        where d.department_id = 1
          and d.department_name = 'mieu本店'
          and d.is_active = true
    ) then
        raise exception 'target_department_not_found:mieu本店';
    end if;

    if exists (
        select 1
        from public.store_item_master i
        where i.department_id = 1
          and i.item_name = '1000'
    ) then
        raise exception 'rollback_name_conflict:1000';
    end if;

    if not exists (
        select 1
        from public.store_item_master i
        where i.department_id = 1
          and i.item_name = 'ドリンク1000'
          and i.item_type = 'standard'
    ) then
        raise exception 'rollback_source_not_found:ドリンク1000';
    end if;
end;
$$;

create temporary table prosper_mieu_product_rollback_names (
    item_name text primary key
) on commit drop;

insert into prosper_mieu_product_rollback_names (item_name)
values
    ('CAMUS VSOP'),
    ('Hennessy VSOP'),
    ('Hennessy XO'),
    ('VOGA（赤）'),
    ('CARNIVOR（赤）'),
    ('MOMA（白）'),
    ('ヴーヴイエロー'),
    ('ヴーヴエクストラ'),
    ('ヴーヴグランダム'),
    ('ローランペリエブリュット'),
    ('ローランペリエロゼ'),
    ('ローランペリエウルトラ'),
    ('モエネクター'),
    ('モエアイス'),
    ('モエアイスロゼ'),
    ('モエN.I.R'),
    ('モエルミナス'),
    ('ベルエポックロゼ'),
    ('ソウメイブリュット'),
    ('ソウメイロゼ'),
    ('アルマンドゴールド'),
    ('エンジェルブラック'),
    ('エンジェルホワイト'),
    ('エンジェルピンク／ブルー'),
    ('ドリンク1500'),
    ('ドリンク2000'),
    ('ドリンク3000'),
    ('枝豆'),
    ('砂肝'),
    ('ナゲット'),
    ('たこ焼き'),
    ('焼きそば'),
    ('チャーハン'),
    ('ふんわり鏡月'),
    ('CHOYA梅'),
    ('れんと'),
    ('鍛高譚'),
    ('鍛高譚梅'),
    ('茜霧島'),
    ('赤霧島'),
    ('鳥飼'),
    ('吉四六'),
    ('Four Roses'),
    ('CHIVAS REGAL'),
    ('I.W. HARPER'),
    ('Maker''s Mark'),
    ('CHIVAS REGAL MIZUNARA'),
    ('知多'),
    ('山崎'),
    ('白州'),
    ('MACALLAN 12');

do $$
declare
    v_delete_count integer;
begin
    select count(*)::integer
      into v_delete_count
    from public.store_item_master i
    join prosper_mieu_product_rollback_names r
      on r.item_name = i.item_name
    where i.department_id = 1
      and i.item_type = 'standard';

    if v_delete_count <> 51 then
        raise exception 'rollback_item_count_mismatch:%', v_delete_count;
    end if;
end;
$$;

delete from public.store_item_master i
using prosper_mieu_product_rollback_names r
where i.department_id = 1
  and i.item_type = 'standard'
  and i.item_name = r.item_name;

update public.store_item_master i
   set item_name = '1000',
       updated_at = now()
 where i.department_id = 1
   and i.item_name = 'ドリンク1000'
   and i.item_type = 'standard';

delete from public.store_item_category_master c
where c.department_id = 1
  and c.category_code = 'bottle'
  and not exists (
      select 1
      from public.store_item_master i
      where i.company_id = c.company_id
        and i.department_id = c.department_id
        and i.item_category_id = c.item_category_id
  );

do $$
declare
    v_standard_count integer;
begin
    select count(*)::integer
      into v_standard_count
    from public.store_item_master i
    where i.department_id = 1
      and i.item_type = 'standard';

    if v_standard_count <> 1 then
        raise exception 'rollback_standard_item_count_mismatch:%', v_standard_count;
    end if;

    if not exists (
        select 1
        from public.store_item_master i
        where i.department_id = 1
          and i.item_name = '1000'
          and i.item_type = 'standard'
          and i.default_price = 1000
          and i.is_cast_back_target = true
          and i.cast_back_regular_unit_amount = 200
          and i.cast_back_nomination_unit_amount = 300
    ) then
        raise exception 'rollback_restored_item_mismatch:1000';
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
    ) as standard_item_count
from public.department_master d
where d.department_id = 1
  and d.department_name = 'mieu本店';

commit;
