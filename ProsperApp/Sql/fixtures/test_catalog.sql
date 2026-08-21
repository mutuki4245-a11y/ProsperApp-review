-- テスト用DB向けのカタログ一式。本番へは絶対に流さないでください。
--
-- Sql/apply_all.sh --with-test-fixtures からのみ呼ばれます。
--
-- 本番の商品マスタseed（mieu_honten_product_master_seed.sql）は、既に
-- 「ドリンク」「フード」「シャンパン」カテゴリと特定の商品が存在することを前提にした
-- 一度きりの移行スクリプトなので、空のDBには当てられません。レビュー環境で
-- 伝票→注文→会計→締めまで通せるように、最低限のカタログをここで用意します。
--
-- 金額や品目は動作確認用のダミーで、本番の価格表とは一致しません。
-- カタログが既に入っているDBでは何もしません。

do $$
declare
    v_company_id  bigint := 1;
    v_department_id bigint := 1;
    v_drink_category bigint;
    v_food_category bigint;
    v_bottle_category bigint;
begin
    if exists (
        select 1
        from public.store_item_master
        where company_id = v_company_id
          and department_id = v_department_id
          and item_type = 'standard'
    ) then
        raise notice 'catalog already present; skipping test fixtures';
        return;
    end if;

    insert into public.store_item_category_master
        (company_id, department_id, category_code, category_name, sort_order)
    values
        (v_company_id, v_department_id, 'drink', 'ドリンク', 10),
        (v_company_id, v_department_id, 'food', 'フード', 20),
        (v_company_id, v_department_id, 'bottle', 'ボトル', 30)
    on conflict (company_id, department_id, category_code) do nothing;

    select item_category_id into v_drink_category
      from public.store_item_category_master
     where company_id = v_company_id and department_id = v_department_id
       and category_code = 'drink';

    select item_category_id into v_food_category
      from public.store_item_category_master
     where company_id = v_company_id and department_id = v_department_id
       and category_code = 'food';

    select item_category_id into v_bottle_category
      from public.store_item_category_master
     where company_id = v_company_id and department_id = v_department_id
       and category_code = 'bottle';

    insert into public.store_item_master (
        company_id, department_id, item_category_id, item_name, item_type,
        default_price, is_cast_back_target,
        cast_back_regular_unit_amount, cast_back_nomination_unit_amount,
        cast_back_type, sort_order
    )
    values
        (v_company_id, v_department_id, v_drink_category, 'ドリンク1000', 'standard', 1000, true,  200, 300, 'drink', 10),
        (v_company_id, v_department_id, v_drink_category, 'ビール',       'standard',  800, false,   0,   0, 'drink', 20),
        (v_company_id, v_department_id, v_drink_category, 'ソフトドリンク', 'standard', 600, false,   0,   0, 'drink', 30),
        (v_company_id, v_department_id, v_food_category,  'フルーツ盛り',  'standard', 2000, false,   0,   0, 'other', 40),
        (v_company_id, v_department_id, v_food_category,  'ナッツ',        'standard',  500, false,   0,   0, 'other', 50),
        (v_company_id, v_department_id, v_bottle_category,'ハウスボトル',  'standard', 12000, true, 1000, 1500, 'sales', 60)
    on conflict (company_id, department_id, item_name) do nothing;

    insert into public.cast_master
        (company_id, department_id, cast_code, display_name, sort_order)
    values
        (v_company_id, v_department_id, 'C001', 'テストキャストA', 10),
        (v_company_id, v_department_id, 'C002', 'テストキャストB', 20),
        (v_company_id, v_department_id, 'C003', 'テストキャストC', 30)
    on conflict (company_id, department_id, cast_code) do nothing;

    insert into public.store_staff_master
        (company_id, department_id, staff_code, display_name, employment_type, sort_order)
    values
        (v_company_id, v_department_id, 'S001', 'テストスタッフA', 'employee', 10),
        (v_company_id, v_department_id, 'S002', 'テストスタッフB', 'part_time', 20)
    on conflict (company_id, department_id, staff_code) do nothing;

    insert into public.store_nomination_back_master (
        company_id, department_id, nomination_kind, nomination_type,
        display_name, companion_time, back_unit_amount, sort_order
    )
    values
        (v_company_id, v_department_id, 'nomination', 'nomination', '本指名',  null, 1000, 10),
        (v_company_id, v_department_id, 'in_store',   'in_store',   '場内指名', null,  500, 20)
    on conflict (company_id, department_id, nomination_kind) do nothing;

    insert into public.store_pricing_plan_master (
        company_id, department_id, pricing_mode, plan_version,
        set_minutes, extension_minutes,
        set_unit_price_single, set_unit_price_per_customer,
        extension_unit_price_single, extension_unit_price_per_customer,
        is_active
    )
    values (
        v_company_id, v_department_id, 'set_extension_v1', 1,
        60, 30,
        4000, 3000,
        1500, 1000,
        true
    );

    -- セット料金・延長料金のシステム商品を、料金プランに合わせて作ります。
    perform store.ensure_pricing_system_items(v_department_id);
end;
$$;
