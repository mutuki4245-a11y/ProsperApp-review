-- ProsperApp RPCs are executed only through the prosper-rpc Edge Function.
-- Direct PostgREST RPC execution is not an application route.
revoke usage on schema store from public, anon, authenticated, service_role;
revoke execute on all functions in schema store from public, anon, authenticated, service_role;

alter default privileges in schema store revoke execute on functions from public;
alter default privileges in schema store revoke execute on functions from anon, authenticated, service_role;
