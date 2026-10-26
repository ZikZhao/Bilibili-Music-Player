-- Regression fixtures exist only in this transaction; no emails or accounts are
-- created through Auth, and ROLLBACK removes all fixture rows.
begin;
insert into auth.users(id,aud,role,email,created_at,updated_at) values
('10000000-0000-4000-8000-000000000001','authenticated','authenticated','library-a@example.invalid',now(),now()),
('10000000-0000-4000-8000-000000000002','authenticated','authenticated','library-b@example.invalid',now(),now());
set local role authenticated;
select set_config('request.jwt.claim.sub','10000000-0000-4000-8000-000000000001',true);
select set_config('request.jwt.claims','{"sub":"10000000-0000-4000-8000-000000000001","role":"authenticated"}',true);
do $$
declare
    doc jsonb := '{"version":1,"media":[{"id":"20000000-0000-4000-8000-000000000001","bvid":"BV_fixture","cid":1,"title":"fixture","artist":"","cover":"","durationSeconds":1,"duration":"0:01"}],"nodes":[{"id":"30000000-0000-4000-8000-000000000001","kind":"playlist","name":"source","parentId":null,"order":0},{"id":"30000000-0000-4000-8000-000000000002","kind":"playlist","name":"linked","parentId":null,"order":1}],"entries":[{"id":"40000000-0000-4000-8000-000000000001","playlistId":"30000000-0000-4000-8000-000000000001","mediaId":"20000000-0000-4000-8000-000000000001","linkedPlaylistId":null,"order":0},{"id":"40000000-0000-4000-8000-000000000002","playlistId":"30000000-0000-4000-8000-000000000002","mediaId":null,"linkedPlaylistId":"30000000-0000-4000-8000-000000000001","order":0}],"likes":[{"mediaId":"20000000-0000-4000-8000-000000000001","addedAt":"2026-10-26T00:00:00Z"}]}';
    invalid jsonb;
    result jsonb;
begin
    result := public.save_music_library(0,doc);
    if (result->>'revision')::bigint <> 1 then raise exception 'initial commit failed'; end if;
    if (select count(*) from public.music_libraries) <> 1 then raise exception 'own row not readable'; end if;
    begin
        perform public.save_music_library(0,doc);
        raise exception 'stale revision was accepted';
    exception when serialization_failure then null; end;
    invalid := jsonb_set(doc,'{entries}',(doc->'entries') || '[{"id":"40000000-0000-4000-8000-000000000003","playlistId":"30000000-0000-4000-8000-000000000001","mediaId":null,"linkedPlaylistId":"30000000-0000-4000-8000-000000000002","order":1}]');
    begin
        perform public.save_music_library(1,invalid);
        raise exception 'dynamic cycle was accepted';
    exception when invalid_parameter_value then null; end;
    invalid := jsonb_set(doc,'{nodes}', '[{"id":"30000000-0000-4000-8000-000000000001","kind":"folder","name":"a","parentId":"30000000-0000-4000-8000-000000000002","order":0},{"id":"30000000-0000-4000-8000-000000000002","kind":"folder","name":"b","parentId":"30000000-0000-4000-8000-000000000001","order":1}]');
    invalid := jsonb_set(invalid,'{entries}','[]');
    begin
        perform public.save_music_library(1,invalid);
        raise exception 'folder cycle was accepted';
    exception when invalid_parameter_value then null; end;
    invalid := jsonb_set(doc,'{media}','[]');
    begin
        perform public.save_music_library(1,invalid);
        raise exception 'dangling references were accepted';
    exception when invalid_parameter_value then null; end;
    if (select revision from public.music_libraries) <> 1 then raise exception 'failed commit changed revision'; end if;
    begin
        update public.music_libraries set document='{}';
        raise exception 'direct table writes were allowed';
    exception when insufficient_privilege then null; end;
end $$;
select set_config('request.jwt.claim.sub','10000000-0000-4000-8000-000000000002',true);
select set_config('request.jwt.claims','{"sub":"10000000-0000-4000-8000-000000000002","role":"authenticated"}',true);
do $$ begin
    if (select count(*) from public.music_libraries) <> 0 then raise exception 'cross-user RLS leak'; end if;
    perform public.save_music_library(0,'{"version":1,"media":[],"nodes":[],"entries":[],"likes":[]}');
    if (select count(*) from public.music_libraries) <> 1 then raise exception 'second account commit failed'; end if;
    if (select user_id from public.music_libraries) <> auth.uid() then raise exception 'wrong account owner'; end if;
end $$;
reset role;
create table public._music_library_rls_probe(id integer);
do $$ begin
    if not (select relrowsecurity from pg_class where oid='public._music_library_rls_probe'::regclass) then
        raise exception 'automatic RLS stopped working after restricting trigger execution';
    end if;
end $$;
do $$ begin
    if (select count(*) from public.music_libraries) <> 2 then raise exception 'accounts overwrote each other'; end if;
    if has_function_privilege('anon','public.save_music_library(bigint,jsonb)','execute') then raise exception 'anonymous RPC access'; end if;
    if has_table_privilege('anon','public.music_libraries','select') then raise exception 'anonymous read access'; end if;
end $$;
select 'PASS: atomic saves, revision conflicts, graph validation, RLS isolation and API privileges' as result;
rollback;
