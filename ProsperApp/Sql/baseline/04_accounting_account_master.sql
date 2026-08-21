-- 勘定科目マスタのbaseline。
--
-- accounting.account_master は会計側で先に整備されていたため、これまで
-- リポジトリには定義も中身もありませんでした。quick_entry_account_master_updates.sql が
-- 補助科目をここの科目コードへ紐付けるので、空のDBではFK違反で止まります。
--
-- 標準的な勘定科目表そのもので、業務データではありません。
-- 既に科目が1件でも入っているDBでは何もしないので、本番の科目名や
-- 並び順を上書きすることはありません。

do $$
begin
    if exists (select 1 from accounting.account_master) then
        raise notice 'account_master already populated; skipping baseline chart of accounts';
        return;
    end if;

    insert into accounting.account_master
        (account_code, account_name, category, dc_type, requires_subaccount, is_active, sort_order, search_key)
    values
    ('1010', '現金', 'asset', 'D', false, true, 10, 'genkin'),
    ('1020', '普通預金', 'asset', 'D', false, true, 20, 'hutuuyokin'),
    ('1030', '当座預金', 'asset', 'D', false, true, 30, 'touzayokin'),
    ('1110', '売掛金', 'asset', 'D', false, true, 40, 'urikakekin'),
    ('1120', '未収入金', 'asset', 'D', false, true, 50, 'misyuunyukin'),
    ('1130', '立替金', 'asset', 'D', false, true, 60, 'tatekaekin'),
    ('1140', '仮払金', 'asset', 'D', false, true, 70, 'karibaraikin'),
    ('1150', '前払費用', 'asset', 'D', false, true, 80, 'maebaraihiyou'),
    ('1160', '前渡金', 'asset', 'D', false, true, 90, 'maewatasikin'),
    ('1210', '貯蔵品', 'asset', 'D', false, true, 100, 'tyouzouhin'),
    ('1310', '備品', 'asset', 'D', false, true, 110, 'bihin'),
    ('1320', '建物附属設備', 'asset', 'D', false, true, 120, 'tatemonohuzokusetsubi'),
    ('1910', '差入保証金', 'asset', 'D', false, true, 130, 'sasiirehosyoukin'),
    ('2010', '買掛金', 'liability', 'C', false, true, 200, 'kaikakekin'),
    ('2020', '未払金', 'liability', 'C', false, true, 210, 'mibaraikin'),
    ('2030', '未払費用', 'liability', 'C', false, true, 220, 'mibaraihiyou'),
    ('2040', '預り金', 'liability', 'C', false, true, 230, 'azukarikin'),
    ('2050', '仮受金', 'liability', 'C', false, true, 240, 'kariukekin'),
    ('2060', '前受金', 'liability', 'C', false, true, 250, 'maeukekin'),
    ('2110', '未払法人税等', 'liability', 'C', false, true, 260, 'mibaraihouzinzeitou'),
    ('2120', '未払消費税等', 'liability', 'C', false, true, 270, 'mibaraishouhizeitou'),
    ('2210', '借入金', 'liability', 'C', false, true, 280, 'kariirekin'),
    ('2220', '役員借入金', 'liability', 'C', true, true, 285, 'yakuinkariire'),
    ('3010', '資本金', 'equity', 'C', false, true, 300, 'sihonkin'),
    ('3020', '繰越利益剰余金', 'equity', 'C', false, true, 310, 'kurikosirieikizyouyokin'),
    ('4010', '売上高', 'revenue', 'C', false, true, 400, 'uriagedaka'),
    ('4020', '雑収入', 'revenue', 'C', false, true, 410, 'zatusyuunyuu'),
    ('4030', '受取利息', 'revenue', 'C', false, true, 420, 'uketoririesoku'),
    ('5010', '仕入高', 'expense', 'D', false, true, 500, 'siiredaka'),
    ('5110', '給料手当', 'expense', 'D', false, true, 510, 'kyuuryouteate'),
    ('5120', '法定福利費', 'expense', 'D', false, true, 520, 'houteihukurihi'),
    ('5130', '福利厚生費', 'expense', 'D', false, true, 530, 'hukurikouseihi'),
    ('5140', '外注費', 'expense', 'D', false, true, 540, 'gaityuuhi'),
    ('5150', '広告宣伝費', 'expense', 'D', false, true, 550, 'koukokusendenhi'),
    ('5160', '旅費交通費', 'expense', 'D', false, true, 560, 'ryohikoutsuuhi'),
    ('5170', '通信費', 'expense', 'D', false, true, 570, 'tuusinhi'),
    ('5180', '消耗品費', 'expense', 'D', false, true, 580, 'syoumouhinhi'),
    ('5190', '水道光熱費', 'expense', 'D', false, true, 590, 'suidoukounetuhi'),
    ('5200', '地代家賃', 'expense', 'D', false, true, 600, 'tidaiyatin'),
    ('5210', 'リース料', 'expense', 'D', false, true, 610, 'riisuryou'),
    ('5220', '租税公課', 'expense', 'D', false, true, 620, 'sozeikouka'),
    ('5230', '接待交際費', 'expense', 'D', false, true, 630, 'settaikousaihi'),
    ('5240', '会議費', 'expense', 'D', false, true, 640, 'kaigihi'),
    ('5250', '衛生費', 'expense', 'D', false, true, 650, 'eiseihi'),
    ('5260', '減価償却費', 'expense', 'D', false, true, 660, 'genkasyoukyakuhi'),
    ('5270', '支払利息', 'expense', 'D', false, true, 670, 'siharairiesoku'),
    ('5280', '倒産防止共済掛金', 'expense', 'D', false, true, 680, 'tousanbousikyousaikakekin'),
    ('5290', '保険料', 'expense', 'D', false, true, 690, 'hokenryou'),
    ('5300', '支払手数料', 'expense', 'D', false, true, 5300, 'siharaitesuryou'),
    ('5310', '販売促進費', 'expense', 'D', false, true, 5310, 'hanbaisokusinhi');
end;
$$;
