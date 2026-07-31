begin;

update public.company_master
   set invoice_registration_number = 'T2010903003088'
 where invoice_registration_number is null
    or trim(invoice_registration_number) = ''
    or trim(invoice_registration_number) = 'T1234567890123';

update public.department_master
   set receipt_display_name = case
           when receipt_display_name is null
             or trim(receipt_display_name) = ''
             or trim(receipt_display_name) = '【テスト店舗】'
           then coalesce(nullif(trim(department_name), ''), '未設定')
           else receipt_display_name
       end,
       receipt_address = case
           when receipt_address is null
             or trim(receipt_address) = ''
             or trim(receipt_address) = '東京都テスト区テスト1-2-3'
           then '東京都府中市宮西町1-11-1、2階'
           else receipt_address
       end,
       receipt_phone = case
           when receipt_phone is null
             or trim(receipt_phone) = ''
             or trim(receipt_phone) = '00-1234-5678'
           then '042-319-2461'
           else receipt_phone
       end,
       receipt_logo = case
           when receipt_logo is null
             or trim(receipt_logo) = ''
             or trim(receipt_logo) = '【テストロゴ】'
           then ''
           else receipt_logo
       end
 where receipt_display_name is null or trim(receipt_display_name) = ''
    or trim(receipt_display_name) = '【テスト店舗】'
    or receipt_address is null or trim(receipt_address) = ''
    or trim(receipt_address) = '東京都テスト区テスト1-2-3'
    or receipt_phone is null or trim(receipt_phone) = ''
    or trim(receipt_phone) = '00-1234-5678'
    or receipt_logo is null or trim(receipt_logo) = ''
    or trim(receipt_logo) = '【テストロゴ】';

update public.store_checkouts checkout
   set issuer_snapshot = jsonb_build_object(
        'schema_version', 'issuer-v1',
        'company_name', coalesce(nullif(trim(company.company_name), ''), '未設定'),
        'invoice_registration_number', coalesce(nullif(trim(company.invoice_registration_number), ''), '未設定'),
        'store_name', coalesce(nullif(trim(department.receipt_display_name), ''), '未設定'),
        'address', coalesce(nullif(trim(department.receipt_address), ''), '未設定'),
        'phone', coalesce(nullif(trim(department.receipt_phone), ''), '未設定'),
        'logo', coalesce(department.receipt_logo, '')
    )
  from public.company_master company
  join public.department_master department
    on department.company_id = company.company_id
 where checkout.company_id = company.company_id
   and checkout.department_id = department.department_id
   and (
        checkout.issuer_snapshot->>'invoice_registration_number' = 'T1234567890123'
     or checkout.issuer_snapshot->>'store_name' = '【テスト店舗】'
     or checkout.issuer_snapshot->>'address' = '東京都テスト区テスト1-2-3'
     or checkout.issuer_snapshot->>'phone' = '00-1234-5678'
     or checkout.issuer_snapshot->>'logo' = '【テストロゴ】'
   );

commit;
