-- CastRaceApp のRLSと権限。ProsperAppのスキーマではありません。
-- cast_race_schema.sql と同じ理由でここに置いています。
--
-- 移管元 cast_race_newbee の public スキーマと同じ内容を、スキーマ修飾した形。
-- create policy に if not exists が無いので drop してから作ります。

-- 読み取り
drop policy if exists "anon read stores" on cast_race.stores;
create policy "anon read stores" on cast_race.stores
  for select to anon using (true);

drop policy if exists "anon read casts" on cast_race.casts;
create policy "anon read casts" on cast_race.casts
  for select to anon using (true);

drop policy if exists "anon read race settings" on cast_race.race_settings;
create policy "anon read race settings" on cast_race.race_settings
  for select to anon using (true);

drop policy if exists "anon read sales entries" on cast_race.sales_entries;
create policy "anon read sales entries" on cast_race.sales_entries
  for select to anon using (true);

drop policy if exists "anon read store daily saves" on cast_race.store_daily_saves;
create policy "anon read store daily saves" on cast_race.store_daily_saves
  for select to anon using (true);

-- 書き込みは会期内かつ有効な店舗・キャストに限る
drop policy if exists "anon insert valid sales entries" on cast_race.sales_entries;
create policy "anon insert valid sales entries" on cast_race.sales_entries
  for insert to anon with check (
    amount >= 0
    and exists (
      select 1 from cast_race.race_settings rs
      where sales_entries.business_date >= rs.start_date
        and sales_entries.business_date <= rs.end_date
    )
    and exists (
      select 1 from cast_race.stores s
      where s.id = sales_entries.store_id and s.is_active
    )
    and exists (
      select 1 from cast_race.casts c
      where c.id = sales_entries.cast_id
        and c.store_id = sales_entries.store_id
        and c.is_active
    )
  );

drop policy if exists "anon update valid sales entries" on cast_race.sales_entries;
create policy "anon update valid sales entries" on cast_race.sales_entries
  for update to anon using (
    amount >= 0
    and exists (
      select 1 from cast_race.race_settings rs
      where sales_entries.business_date >= rs.start_date
        and sales_entries.business_date <= rs.end_date
    )
    and exists (
      select 1 from cast_race.stores s
      where s.id = sales_entries.store_id and s.is_active
    )
    and exists (
      select 1 from cast_race.casts c
      where c.id = sales_entries.cast_id
        and c.store_id = sales_entries.store_id
        and c.is_active
    )
  ) with check (
    amount >= 0
    and exists (
      select 1 from cast_race.race_settings rs
      where sales_entries.business_date >= rs.start_date
        and sales_entries.business_date <= rs.end_date
    )
    and exists (
      select 1 from cast_race.stores s
      where s.id = sales_entries.store_id and s.is_active
    )
    and exists (
      select 1 from cast_race.casts c
      where c.id = sales_entries.cast_id
        and c.store_id = sales_entries.store_id
        and c.is_active
    )
  );

drop policy if exists "anon insert valid store daily saves" on cast_race.store_daily_saves;
create policy "anon insert valid store daily saves" on cast_race.store_daily_saves
  for insert to anon with check (
    exists (
      select 1 from cast_race.race_settings rs
      where store_daily_saves.business_date >= rs.start_date
        and store_daily_saves.business_date <= rs.end_date
    )
    and exists (
      select 1 from cast_race.stores s
      where s.id = store_daily_saves.store_id and s.is_active
    )
  );

drop policy if exists "anon update valid store daily saves" on cast_race.store_daily_saves;
create policy "anon update valid store daily saves" on cast_race.store_daily_saves
  for update to anon using (
    exists (
      select 1 from cast_race.race_settings rs
      where store_daily_saves.business_date >= rs.start_date
        and store_daily_saves.business_date <= rs.end_date
    )
    and exists (
      select 1 from cast_race.stores s
      where s.id = store_daily_saves.store_id and s.is_active
    )
  ) with check (
    exists (
      select 1 from cast_race.race_settings rs
      where store_daily_saves.business_date >= rs.start_date
        and store_daily_saves.business_date <= rs.end_date
    )
    and exists (
      select 1 from cast_race.stores s
      where s.id = store_daily_saves.store_id and s.is_active
    )
  );

-- 権限。移管元は public スキーマの既定grantに乗っていたが、専用スキーマでは明示する。
-- NightQueenGP の schema.sql と同じ考え方で、必要な分だけ与える。
revoke all on all tables in schema cast_race from anon, authenticated;
grant usage on schema cast_race to anon, authenticated;
grant select on
  cast_race.stores,
  cast_race.casts,
  cast_race.race_settings,
  cast_race.sales_entries,
  cast_race.store_daily_saves
  to anon, authenticated;
grant insert, update on
  cast_race.sales_entries,
  cast_race.store_daily_saves
  to anon, authenticated;
