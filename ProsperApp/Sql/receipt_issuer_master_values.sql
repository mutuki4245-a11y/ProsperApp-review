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

commit;
