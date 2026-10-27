-- Run in the Supabase SQL editor or execute_sql. The transaction changes no data.
begin;
do $$
declare
    claims jsonb := '{"email":"test@example.invalid","is_anonymous":false,"amr":[{"method":"otp"}],"keep":{"custom":"unchanged"}}';
    event jsonb;
    result jsonb;
    method text;
begin
    foreach method in array array['otp','email/signup','token_refresh'] loop
        event := jsonb_build_object('claims',claims,'authentication_method',method);
        result := music_auth.enforce_email_otp(event);
        if result->'claims' is distinct from claims or result ? 'error' then
            raise exception 'allowed flow rejected or claims altered: %', method;
        end if;
    end loop;
    foreach method in array array['password','recovery','oauth','anonymous','magiclink','unknown'] loop
        result := music_auth.enforce_email_otp(jsonb_build_object('claims',claims,'authentication_method',method));
        if result->'error'->>'http_code' is distinct from '403' or result ? 'claims' then
            raise exception 'disallowed flow accepted: %', method;
        end if;
    end loop;
    foreach claims in array array[
        '{"email":"test@example.invalid","is_anonymous":false,"amr":[{"method":"password"}]}'::jsonb,
        '{"email":"test@example.invalid","is_anonymous":false}'::jsonb,
        '{"email":"test@example.invalid","is_anonymous":false,"amr":{}}'::jsonb,
        '{"email":"","is_anonymous":false,"amr":[{"method":"otp"}]}'::jsonb,
        '{"email":"test@example.invalid","is_anonymous":true,"amr":[{"method":"otp"}]}'::jsonb
    ] loop
        result := music_auth.enforce_email_otp(jsonb_build_object('claims',claims,'authentication_method','token_refresh'));
        if result->'error'->>'http_code' is distinct from '403' then raise exception 'unsafe refresh accepted'; end if;
    end loop;
    if not (music_auth.enforce_email_otp('{}'::jsonb) ? 'error') then raise exception 'missing payload accepted'; end if;
end $$;
do $$ begin
    if not has_function_privilege('supabase_auth_admin','music_auth.enforce_email_otp(jsonb)','execute')
       or not has_schema_privilege('supabase_auth_admin','music_auth','usage')
       or has_function_privilege('anon','music_auth.enforce_email_otp(jsonb)','execute')
       or has_function_privilege('authenticated','music_auth.enforce_email_otp(jsonb)','execute')
       or has_schema_privilege('anon','music_auth','usage')
       or has_schema_privilege('authenticated','music_auth','usage') then
        raise exception 'Auth hook exposed to clients';
    end if;
end $$;
select 'PASS: OTP, first signup, refresh, password/recovery rejection, preserved claims and private grants' as result;
rollback;
