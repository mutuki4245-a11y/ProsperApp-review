begin;

create or replace function store.get_payment_methods(
    p_department_id bigint
)
returns table (
    method_code text,
    method_name text,
    requires_received_amount boolean,
    sort_order integer
)
language sql
security definer
set search_path = public
as $$
    select
        payment_method.payment_method_code,
        payment_method.payment_method_name,
        payment_method.requires_received_amount,
        payment_method.sort_order
      from public.payment_method_master payment_method
     where payment_method.department_id = p_department_id
       and payment_method.is_active = true
     order by payment_method.sort_order, payment_method.payment_method_id;
$$;

revoke all on function store.get_payment_methods(bigint)
    from public, anon, authenticated, service_role;

commit;
