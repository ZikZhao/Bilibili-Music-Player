-- Private-by-default, account-scoped library snapshots. Metadata only; no audio,
-- passwords, refresh tokens or temporary streaming URLs belong in this table.
create schema if not exists music_private;
revoke all on schema music_private from public, anon;
grant usage on schema music_private to authenticated;

create table public.music_libraries (
    user_id uuid primary key references auth.users(id) on delete cascade,
    revision bigint not null check (revision > 0),
    document jsonb not null,
    updated_at timestamptz not null default now()
);
alter table public.music_libraries enable row level security;
revoke all on public.music_libraries from public, anon, authenticated;
grant select on public.music_libraries to authenticated;
create policy "read own music library" on public.music_libraries
    for select to authenticated using ((select auth.uid()) = user_id);

create function music_private.validate_library(d jsonb) returns void
language plpgsql set search_path = '' as $$
declare k text;
begin
    if d is null or jsonb_typeof(d) <> 'object' or (d->>'version')::int is distinct from 1
       or octet_length(d::text) > 12000000 then
        raise exception 'invalid_library_document' using errcode = '22023';
    end if;
    foreach k in array array['media','nodes','entries','likes'] loop
        if coalesce(jsonb_typeof(d->k), '') <> 'array' then
            raise exception 'invalid_library_array' using errcode = '22023';
        end if;
    end loop;
    if jsonb_array_length(d->'nodes') > 5000 or jsonb_array_length(d->'media') > 100000
       or jsonb_array_length(d->'entries') > 300000 or jsonb_array_length(d->'likes') > 100000 then
        raise exception 'library_too_large' using errcode = '22023';
    end if;

    if exists (select 1 from jsonb_to_recordset(d->'nodes') as n(id uuid,kind text,name text,"parentId" uuid)
        where n.id is null or n.id = '00000000-0000-0000-0000-000000000000'::uuid
           or n.kind is null or n.kind not in ('folder','playlist') or coalesce(length(btrim(n.name)),0) not between 1 and 120
           or (n."parentId" is not null and not exists (
               select 1 from jsonb_to_recordset(d->'nodes') as p(id uuid,kind text)
               where p.id = n."parentId" and p.kind = 'folder')))
       or exists (select 1 from jsonb_to_recordset(d->'nodes') as n(id uuid) group by id having count(*) > 1) then
        raise exception 'invalid_library_nodes' using errcode = '22023';
    end if;

    if exists (select 1 from jsonb_to_recordset(d->'media') as m(id uuid,bvid text,cid bigint,title text,"durationSeconds" bigint)
        where m.id is null or m.id = '00000000-0000-0000-0000-000000000000'::uuid
           or coalesce(length(btrim(m.bvid)),0) not between 1 and 64 or coalesce(m.cid,-1) < 0
           or coalesce(m."durationSeconds",-1) < 0 or m.title is null)
       or exists (select 1 from jsonb_to_recordset(d->'media') as m(id uuid) group by id having count(*) > 1)
       or exists (select 1 from jsonb_to_recordset(d->'media') as m(bvid text,cid bigint) group by bvid,cid having count(*) > 1) then
        raise exception 'invalid_library_media' using errcode = '22023';
    end if;

    if exists (select 1 from jsonb_to_recordset(d->'entries') as e(id uuid,"playlistId" uuid,"mediaId" uuid,"linkedPlaylistId" uuid)
        where e.id is null or e.id = '00000000-0000-0000-0000-000000000000'::uuid
           or (e."mediaId" is null) = (e."linkedPlaylistId" is null)
           or not exists (select 1 from jsonb_to_recordset(d->'nodes') as n(id uuid,kind text) where n.id=e."playlistId" and n.kind='playlist')
           or (e."mediaId" is not null and not exists (select 1 from jsonb_to_recordset(d->'media') as m(id uuid) where m.id=e."mediaId"))
           or (e."linkedPlaylistId" is not null and not exists (
               select 1 from jsonb_to_recordset(d->'nodes') as n(id uuid,kind text) where n.id=e."linkedPlaylistId" and n.kind='playlist')))
       or exists (select 1 from jsonb_to_recordset(d->'entries') as e(id uuid) group by id having count(*) > 1)
       or exists (select 1 from jsonb_to_recordset(d->'entries') as e("playlistId" uuid,"mediaId" uuid,"linkedPlaylistId" uuid)
                  group by "playlistId","mediaId","linkedPlaylistId" having count(*) > 1) then
        raise exception 'invalid_library_entries' using errcode = '22023';
    end if;

    if exists (select 1 from jsonb_to_recordset(d->'likes') as l("mediaId" uuid,"addedAt" timestamptz)
        where l."addedAt" is null or not exists (select 1 from jsonb_to_recordset(d->'media') as m(id uuid) where m.id=l."mediaId"))
       or exists (select 1 from jsonb_to_recordset(d->'likes') as l("mediaId" uuid) group by "mediaId" having count(*) > 1) then
        raise exception 'invalid_library_likes' using errcode = '22023';
    end if;

    -- Separate edge types: directory ancestry and dynamic playlist references.
    -- UNION removes duplicate traversal states, including converging DAG paths.
    if exists (
        with recursive edges(kind,source,target) as (
            select 'folder', n.id, n."parentId" from jsonb_to_recordset(d->'nodes') as n(id uuid,"parentId" uuid) where n."parentId" is not null
            union all
            select 'link', e."playlistId",e."linkedPlaylistId" from jsonb_to_recordset(d->'entries') as e("playlistId" uuid,"linkedPlaylistId" uuid) where e."linkedPlaylistId" is not null
        ), walk(kind,source,target,depth) as (
            select kind,source,target,1 from edges
            union
            select w.kind,w.source,e.target,w.depth+1 from walk w join edges e on e.kind=w.kind and e.source=w.target
            where w.depth < 65 and w.source <> w.target
        ) select 1 from walk where source=target or depth>64
    ) then
        raise exception 'library_cycle_or_depth_limit' using errcode = '22023';
    end if;
end;
$$;
revoke all on function music_private.validate_library(jsonb) from public, anon, authenticated;

-- Only this narrowly scoped command may write. It cannot accept an owner ID:
-- ownership is derived from the validated session, never from client data.
create function music_private.save_library(expected_revision bigint, library_document jsonb) returns jsonb
language plpgsql security definer set search_path = '' as $$
declare owner_id uuid := auth.uid(); current_revision bigint;
begin
    if owner_id is null then raise exception 'authentication_required' using errcode='42501'; end if;
    if expected_revision is null or expected_revision < 0 then raise exception 'invalid_revision' using errcode='22023'; end if;
    perform pg_advisory_xact_lock(hashtextextended(owner_id::text, 0));
    select revision into current_revision from public.music_libraries where user_id=owner_id;
    current_revision := coalesce(current_revision,0);
    if current_revision <> expected_revision then raise exception 'library_revision_conflict' using errcode='40001'; end if;
    perform music_private.validate_library(library_document);
    insert into public.music_libraries(user_id,revision,document)
        values(owner_id,current_revision+1,library_document)
    on conflict(user_id) do update set revision=excluded.revision,document=excluded.document,updated_at=now();
    return jsonb_build_object('revision',current_revision+1);
end;
$$;
revoke all on function music_private.save_library(bigint,jsonb) from public,anon,authenticated;
grant execute on function music_private.save_library(bigint,jsonb) to authenticated;

create function public.save_music_library(expected_revision bigint, library_document jsonb) returns jsonb
language sql security invoker set search_path = '' as $$
    select music_private.save_library(expected_revision,library_document);
$$;
revoke all on function public.save_music_library(bigint,jsonb) from public,anon,authenticated;
grant execute on function public.save_music_library(bigint,jsonb) to authenticated;

comment on table public.music_libraries is 'Private music library graph; atomic revisioned snapshots, client account is enforced by Auth and RLS.';
comment on function public.save_music_library(bigint,jsonb) is 'Optimistic commit. Rejects stale revisions, dangling references, cycles and oversized graphs.';
