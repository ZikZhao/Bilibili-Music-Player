-- A private Auth hook enforces passwordless access at token issuance, including
-- refreshes of old password-only sessions. No user data or credentials are read.
create schema if not exists music_auth;
revoke all on schema music_auth from public, anon, authenticated;
grant usage on schema music_auth to supabase_auth_admin;

create function music_auth.enforce_email_otp(event jsonb) returns jsonb
language plpgsql immutable security invoker set search_path = '' as $$
declare
    claims jsonb := event->'claims';
    method text := event->>'authentication_method';
    permitted boolean := false;
begin
    if jsonb_typeof(claims) = 'object'
       and coalesce(claims->>'email', '') <> ''
       and claims->>'is_anonymous' = 'false' then
        permitted := method in ('otp', 'email/signup');
        if method = 'token_refresh' then
            permitted := exists (
                select 1 from jsonb_array_elements(
                    case when jsonb_typeof(claims->'amr') = 'array'
                         then claims->'amr' else '[]'::jsonb end
                ) as factor
                where factor->>'method' in ('otp', 'email/signup')
            );
        end if;
    end if;
    if coalesce(permitted, false) then
        return jsonb_build_object('claims', claims);
    end if;
    return jsonb_build_object('error', jsonb_build_object(
        'http_code', 403, 'message', 'email_otp_required'
    ));
end;
$$;
revoke all on function music_auth.enforce_email_otp(jsonb) from public, anon, authenticated;
grant execute on function music_auth.enforce_email_otp(jsonb) to supabase_auth_admin;
comment on function music_auth.enforce_email_otp(jsonb) is
    'Custom access token Auth hook: email OTP/signup and their refreshes only; never a client RPC.';
