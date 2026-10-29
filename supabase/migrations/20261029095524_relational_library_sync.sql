-- One-time development migration. Legacy snapshots remain as a read-only backup.
create table public.music_sync_state(
 owner_id uuid primary key references auth.users(id) on delete cascade,
 revision bigint not null default 0 check(revision>=0),
 min_revision bigint not null default 0, hash text not null default '');
create table public.music_playlists(
 owner_id uuid not null references public.music_sync_state(owner_id) on delete cascade,
 id uuid not null, name text not null check(length(name) between 1 and 120),
 kind text not null check(kind in ('root','default','playlist')), primary key(owner_id,id));
create table public.music_media(
 owner_id uuid not null references public.music_sync_state(owner_id) on delete cascade,
 id uuid not null, bvid text not null check(length(bvid) between 1 and 64), cid bigint not null check(cid>=0),
 title text not null,artist text not null,cover text not null,duration_seconds bigint not null check(duration_seconds>=0),duration text not null,
 primary key(owner_id,id),unique(owner_id,bvid,cid));
create table public.music_playlist_items(
 owner_id uuid not null,parent_id uuid not null,id uuid not null,media_id uuid,child_id uuid,
 rank text collate "C" not null check(length(rank)<=2048),added_at_ms bigint,sequence bigint not null default 0,
 primary key(owner_id,parent_id,id),check((media_id is null)<>(child_id is null)),
 foreign key(owner_id,parent_id) references public.music_playlists(owner_id,id) on delete cascade,
 foreign key(owner_id,child_id) references public.music_playlists(owner_id,id) on delete cascade,
 foreign key(owner_id,media_id) references public.music_media(owner_id,id) on delete cascade);
create unique index music_single_parent on public.music_playlist_items(owner_id,child_id) where child_id is not null;
create unique index music_single_song on public.music_playlist_items(owner_id,parent_id,media_id) where media_id is not null;
create index music_item_order on public.music_playlist_items(owner_id,parent_id,rank);
create index music_item_media on public.music_playlist_items(owner_id,media_id);
create table public.music_sync_writers(
 owner_id uuid not null references public.music_sync_state(owner_id) on delete cascade,
 writer_id uuid not null,last_sequence bigint not null default 0,primary key(owner_id,writer_id));
comment on table public.music_sync_writers is 'Technical outbox stream high-water marks only. Playlist items never have device ownership.';
create table public.music_sync_changes(
 owner_id uuid not null references public.music_sync_state(owner_id) on delete cascade,
 revision bigint not null,previous_hash text not null,hash text not null,patch jsonb not null,
 primary key(owner_id,revision));
do $$ declare t text; begin
 foreach t in array array['music_sync_state','music_playlists','music_media','music_playlist_items','music_sync_writers','music_sync_changes'] loop
  execute format('alter table public.%I enable row level security',t);
  execute format('revoke all on public.%I from public,anon,authenticated',t);
  execute format('grant select on public.%I to authenticated',t);
  execute format('create policy own_library on public.%I for select to authenticated using ((select auth.uid())=owner_id)',t);
 end loop;
end $$;

create function music_private.rank_len(h text) returns integer language plpgsql immutable set search_path='' as $$
begin
 if ascii(h) between 97 and 122 then return ascii(h)-ascii('a')+2; end if;
 if ascii(h) between 65 and 90 then return ascii('Z')-ascii(h)+2; end if;
 raise exception 'invalid_rank_head' using errcode='22023';
end $$;
create function music_private.rank_step(v text,up boolean) returns text language plpgsql immutable set search_path='' as $$
declare digits constant text:='0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz';h text:=left(v,1);tail text:=substr(v,2);i integer;n integer;carry boolean:=true;next_h text;
begin
 for i in reverse length(tail)..1 loop
  n:=strpos(digits,substr(tail,i,1))-1+case when up then 1 else -1 end;
  if n=62 or n=-1 then tail:=overlay(tail placing case when up then '0' else 'z' end from i for 1);
  else tail:=overlay(tail placing substr(digits,n+1,1) from i for 1);carry:=false;exit;end if;
 end loop;
 if not carry then return h||tail;end if;
 if up and h='Z' then return 'a0';end if;
 if not up and h='a' then return 'Zz';end if;
 if (up and h='z') or (not up and h='A') then return null;end if;
 next_h:=chr(ascii(h)+case when up then 1 else -1 end);
 if music_private.rank_len(next_h)>length(v) then return next_h||tail||case when up then '0' else 'z' end;end if;
 return next_h||left(tail,length(tail)-1);
end $$;
create function music_private.rank_mid(a text,b text) returns text language plpgsql immutable set search_path='' as $$
declare digits constant text:='0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz';n integer:=0;l integer;u integer;
begin
 if b is not null then
  while n<length(b) and coalesce(nullif(substr(a,n+1,1),''),'0')=substr(b,n+1,1) loop n:=n+1;end loop;
  if n>0 then return left(b,n)||music_private.rank_mid(substr(a,n+1),substr(b,n+1));end if;
 end if;
 l:=case when length(a)>0 then strpos(digits,left(a,1))-1 else 0 end;
 u:=case when b is not null then strpos(digits,left(b,1))-1 else 62 end;
 if u-l>1 then return substr(digits,(l+u+1)/2+1,1);end if;
 if length(b)>1 then return left(b,1);end if;
 return substr(digits,l+1,1)||music_private.rank_mid(substr(a,2),null);
end $$;
create function music_private.rank_between(a text,b text) returns text language plpgsql immutable set search_path='' as $$
declare ia text;ib text;fa text;fb text;n text;
begin
 if a is not null and b is not null and a collate "C">=b collate "C" then raise exception 'unordered_rank_bounds';end if;
 if a is null then
  if b is null then return 'a0';end if;
  ib:=left(b,music_private.rank_len(left(b,1)));fb:=substr(b,length(ib)+1);
  if ib='A'||repeat('0',26) then return ib||music_private.rank_mid('',fb);end if;
  if length(ib)<length(b) then return ib;end if;
  return music_private.rank_step(ib,false);
 end if;
 ia:=left(a,music_private.rank_len(left(a,1)));fa:=substr(a,length(ia)+1);
 if b is null then return coalesce(music_private.rank_step(ia,true),ia||music_private.rank_mid(fa,null));end if;
 ib:=left(b,music_private.rank_len(left(b,1)));fb:=substr(b,length(ib)+1);
 if ia=ib then return ia||music_private.rank_mid(fa,fb);end if;
 n:=music_private.rank_step(ia,true);
 if n collate "C"<b collate "C" then return n;end if;
 return ia||music_private.rank_mid(fa,null);
end $$;
create function music_private.v2_snapshot(p_owner uuid) returns jsonb language sql stable set search_path='' as $$
 select jsonb_build_object('version',2,
 'media',coalesce((select jsonb_agg(jsonb_build_object('id',id,'bvid',bvid,'cid',cid,'title',title,'artist',artist,'cover',cover,'durationSeconds',duration_seconds,'duration',duration) order by id::text) from public.music_media where owner_id=p_owner),'[]'::jsonb),
 'nodes',coalesce((select jsonb_agg(jsonb_build_object('id',p.id,'kind','playlist','name',p.name,'parentId',case when i.parent_id='00000000-0000-0000-0000-000000000001'::uuid then null else i.parent_id end,'order',0,'rank',i.rank) order by p.id::text) from public.music_playlists p join public.music_playlist_items i on i.owner_id=p.owner_id and i.child_id=p.id where p.owner_id=p_owner and p.kind='playlist'),'[]'::jsonb),
 'entries',coalesce((select jsonb_agg(jsonb_build_object('id',id,'playlistId',parent_id,'mediaId',media_id,'linkedPlaylistId',null,'order',0,'rank',rank) order by id::text) from public.music_playlist_items where owner_id=p_owner and media_id is not null and parent_id<>'00000000-0000-0000-0000-000000000002'::uuid),'[]'::jsonb),
 'likes',coalesce((select jsonb_agg(jsonb_build_object('mediaId',media_id,'addedAt',to_char(to_timestamp(added_at_ms/1000.0) at time zone 'UTC','YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),'sequence',sequence) order by media_id::text) from public.music_playlist_items where owner_id=p_owner and parent_id='00000000-0000-0000-0000-000000000002'::uuid),'[]'::jsonb));
$$;
create function music_private.b64(v text) returns text language sql immutable set search_path='' as $$ select replace(encode(convert_to(coalesce(v,''),'UTF8'),'base64'),E'\n','') $$;
create function music_private.v2_hash(p_owner uuid) returns text language sql stable set search_path='' as $$
 select encode(sha256(convert_to('library-v2'||E'\n'||
 coalesce((select string_agg('m|'||id::text||'|'||music_private.b64(bvid)||'|'||cid::text||'|'||music_private.b64(title)||'|'||music_private.b64(artist)||'|'||music_private.b64(cover)||'|'||duration_seconds::text||'|'||music_private.b64(duration)||E'\n','' order by id::text) from public.music_media where owner_id=p_owner),'')||
 coalesce((select string_agg('n|'||p.id::text||'|'||music_private.b64(p.name)||'|'||case when i.parent_id='00000000-0000-0000-0000-000000000001'::uuid then '-' else i.parent_id::text end||'|'||i.rank||E'\n','' order by p.id::text) from public.music_playlists p join public.music_playlist_items i on i.owner_id=p.owner_id and i.child_id=p.id where p.owner_id=p_owner and p.kind='playlist'),'')||
 coalesce((select string_agg('e|'||id::text||'|'||parent_id::text||'|'||media_id::text||'|'||rank||E'\n','' order by id::text) from public.music_playlist_items where owner_id=p_owner and media_id is not null and parent_id<>'00000000-0000-0000-0000-000000000002'::uuid),'')||
 coalesce((select string_agg('l|'||media_id::text||'|'||added_at_ms::text||'|'||sequence::text||E'\n','' order by media_id::text) from public.music_playlist_items where owner_id=p_owner and parent_id='00000000-0000-0000-0000-000000000002'::uuid),''),'UTF8')),'hex');
$$;
create function music_private.v2_init(p_owner uuid) returns void language plpgsql set search_path='' as $$
begin
 insert into public.music_sync_state(owner_id) values(p_owner) on conflict do nothing;
 insert into public.music_playlists values
 (p_owner,'00000000-0000-0000-0000-000000000001','收藏库','root'),
 (p_owner,'00000000-0000-0000-0000-000000000002','我喜欢','default') on conflict do nothing;
 update public.music_sync_state set hash=music_private.v2_hash(p_owner) where owner_id=p_owner and hash='';
end $$;
create function music_private.v2_diff(a jsonb,b jsonb) returns jsonb language plpgsql immutable set search_path='' as $$
declare result jsonb:='{}';k text;idkey text;gone text;
begin
 foreach k in array array['media','nodes','entries','likes'] loop
  idkey:=case when k='likes' then 'mediaId' else 'id' end;
  gone:=case k when 'media' then 'removedMedia' when 'nodes' then 'removedNodes' when 'entries' then 'removedEntries' else 'removedLikes' end;
  result:=result||jsonb_build_object(k,coalesce((select jsonb_agg(n.value) from jsonb_array_elements(b->k) n left join jsonb_array_elements(a->k) o on o.value->>idkey=n.value->>idkey where o.value is null or o.value<>n.value),'[]'::jsonb));
  result:=result||jsonb_build_object(gone,coalesce((select jsonb_agg(o.value->>idkey) from jsonb_array_elements(a->k) o left join jsonb_array_elements(b->k) n on n.value->>idkey=o.value->>idkey where n.value is null),'[]'::jsonb));
 end loop;
 return result;
end $$;
create function music_private.v2_apply(p_owner uuid,c jsonb,p_revision bigint) returns void language plpgsql set search_path='' as $$
declare root_id constant uuid:='00000000-0000-0000-0000-000000000001';default_id constant uuid:='00000000-0000-0000-0000-000000000002';
 typ text:=c->>'type';node uuid:=(c->>'nodeId')::uuid;parent uuid:=coalesce((c->>'parentId')::uuid,root_id);
m jsonb:=c->'media';track uuid;newrank text;last_rank text;obj jsonb;item uuid:=(c->>'itemId')::uuid;anchor uuid:=(c->>'anchorId')::uuid;arank text;lo text;hi text;cycle boolean;rec record;parent_depth integer;subtree_height integer;
begin
 if typ='remember' then
  if (m->>'id')::uuid in(root_id,default_id,'00000000-0000-0000-0000-000000000000'::uuid) or length(btrim(m->>'bvid')) not between 1 and 64 or (m->>'cid')::bigint<0 or (m->>'durationSeconds')::bigint<0 or
     length(coalesce(m->>'title',''))>10000 or length(coalesce(m->>'artist',''))>10000 or length(coalesce(m->>'cover',''))>10000 or length(coalesce(m->>'duration',''))>120 then raise exception 'invalid_media' using errcode='22023';end if;
  insert into public.music_media values(p_owner,(m->>'id')::uuid,m->>'bvid',(m->>'cid')::bigint,coalesce(m->>'title',''),coalesce(m->>'artist',''),coalesce(m->>'cover',''),(m->>'durationSeconds')::bigint,coalesce(m->>'duration','')) on conflict do nothing;
 elsif typ='create' then
  if not exists(select 1 from public.music_playlists where owner_id=p_owner and id=parent and kind<>'default') then return;end if;
  if node in(root_id,default_id,'00000000-0000-0000-0000-000000000000'::uuid) then raise exception 'reserved_playlist' using errcode='22023';end if;
  if exists(select 1 from public.music_playlists where owner_id=p_owner and id=node) then return;end if;
  if exists(select 1 from public.music_playlist_items where owner_id=p_owner and parent_id<>default_id and id=node) then return;end if;
  with recursive ancestors(id) as(select parent union all select i.parent_id from public.music_playlist_items i join ancestors a on i.child_id=a.id where i.owner_id=p_owner)
  select count(*)-1 into parent_depth from ancestors;
  if parent_depth>=64 then return;end if;
  select rank into last_rank from public.music_playlist_items where owner_id=p_owner and parent_id=parent order by rank collate "C" desc limit 1;
  newrank:=music_private.rank_between(last_rank,null);
  insert into public.music_playlists values(p_owner,node,btrim(c->>'name'),'playlist');
  insert into public.music_playlist_items(owner_id,parent_id,id,child_id,rank) values(p_owner,parent,node,node,newrank);
 elsif typ='rename' then
  update public.music_playlists set name=btrim(c->>'name') where owner_id=p_owner and id=node and kind='playlist';
 elsif typ='move_node' then
  if not exists(select 1 from public.music_playlists where owner_id=p_owner and id=node and kind='playlist') or
     not exists(select 1 from public.music_playlists where owner_id=p_owner and id=parent and kind<>'default') then return;end if;
  with recursive ancestors(id) as(select parent union all select i.parent_id from public.music_playlist_items i join ancestors a on i.child_id=a.id where i.owner_id=p_owner)
  select exists(select 1 from ancestors where id=node) into cycle;
  if cycle then return;end if;
  with recursive ancestors(id) as(select parent union all select i.parent_id from public.music_playlist_items i join ancestors a on i.child_id=a.id where i.owner_id=p_owner)
  select count(*)-1 into parent_depth from ancestors;
  with recursive subtree(id,depth) as(select node,1 union all select i.child_id,s.depth+1 from public.music_playlist_items i join subtree s on s.id=i.parent_id where i.owner_id=p_owner and i.child_id is not null)
  select max(depth) into subtree_height from subtree;
  if parent_depth+subtree_height>64 then return;end if;
  select rank into last_rank from public.music_playlist_items where owner_id=p_owner and parent_id=parent order by rank collate "C" desc limit 1;
  update public.music_playlist_items set parent_id=parent,rank=music_private.rank_between(last_rank,null) where owner_id=p_owner and child_id=node;
 elsif typ='delete' then
  if not exists(select 1 from public.music_playlists where owner_id=p_owner and id=node and kind='playlist') then return;end if;
  with recursive subtree(id) as(select node union all select i.child_id from public.music_playlist_items i join subtree s on s.id=i.parent_id where i.owner_id=p_owner and i.child_id is not null)
  delete from public.music_playlists where owner_id=p_owner and id in(select id from subtree);
 elsif typ='like' then
  select id into track from public.music_media where owner_id=p_owner and bvid=c->>'bvid' and cid=(c->>'cid')::bigint;
  if track is null then return;end if;
  if (c->>'liked')::boolean then
   if (c->>'occurredAt')::bigint not between -62135596800000 and 253402300799999 then raise exception 'invalid_favorite_time' using errcode='22023';end if;
   insert into public.music_playlist_items(owner_id,parent_id,id,media_id,rank,added_at_ms,sequence)
    values(p_owner,default_id,track,track,'',(c->>'occurredAt')::bigint,p_revision) on conflict do nothing;
  else delete from public.music_playlist_items where owner_id=p_owner and parent_id=default_id and media_id=track;end if;
 elsif typ='add_tracks' then
  if not exists(select 1 from public.music_playlists where owner_id=p_owner and id=parent and kind='playlist') then return;end if;
  for obj in select value from jsonb_array_elements(c->'tracks') loop
   select id into track from public.music_media where owner_id=p_owner and bvid=obj->>'bvid' and cid=(obj->>'cid')::bigint;
   if track is null or exists(select 1 from public.music_playlist_items where owner_id=p_owner and parent_id=parent and media_id=track) then continue;end if;
   if (obj->>'entryId')::uuid in(root_id,default_id,'00000000-0000-0000-0000-000000000000'::uuid) then raise exception 'invalid_entry_id' using errcode='22023';end if;
   if exists(select 1 from public.music_playlist_items where owner_id=p_owner and parent_id<>default_id and id=(obj->>'entryId')::uuid) or exists(select 1 from public.music_playlists where owner_id=p_owner and id=(obj->>'entryId')::uuid) then continue;end if;
   select rank into last_rank from public.music_playlist_items where owner_id=p_owner and parent_id=parent order by rank collate "C" desc limit 1;
   insert into public.music_playlist_items(owner_id,parent_id,id,media_id,rank)
    values(p_owner,parent,(obj->>'entryId')::uuid,track,music_private.rank_between(last_rank,null));
  end loop;
 elsif typ in('remove_track','remove_media') then
  select id into track from public.music_media where owner_id=p_owner and bvid=c->>'bvid' and cid=(c->>'cid')::bigint;
  if typ='remove_media' then delete from public.music_media where owner_id=p_owner and id=track;
  else delete from public.music_playlist_items where owner_id=p_owner and parent_id=parent and media_id=track;end if;
 elsif typ='reorder' then
  select rank into arank from public.music_playlist_items where owner_id=p_owner and parent_id=parent and id=anchor and id<>item;
  if arank is null or parent=default_id or not exists(select 1 from public.music_playlist_items where owner_id=p_owner and parent_id=parent and id=item) then return;end if;
  if (c->>'before')::boolean then
   hi:=arank;select rank into lo from public.music_playlist_items where owner_id=p_owner and parent_id=parent and id<>item and rank collate "C"<arank collate "C" order by rank collate "C" desc limit 1;
  else lo:=arank;select rank into hi from public.music_playlist_items where owner_id=p_owner and parent_id=parent and id<>item and rank collate "C">arank collate "C" order by rank collate "C" limit 1;end if;
  update public.music_playlist_items set rank=music_private.rank_between(lo,hi) where owner_id=p_owner and parent_id=parent and id=item;
 else raise exception 'unknown_operation' using errcode='22023';
 end if;
 -- Rare pathological gap exhaustion; ordinary insert/move changes one rank.
 for rec in select distinct parent_id from public.music_playlist_items where owner_id=p_owner and length(rank)>256 loop
  last_rank:=null;
  for obj in select jsonb_build_object('id',id) from public.music_playlist_items where owner_id=p_owner and parent_id=rec.parent_id order by rank collate "C",id loop
   last_rank:=music_private.rank_between(last_rank,null);
   update public.music_playlist_items set rank=last_rank where owner_id=p_owner and parent_id=rec.parent_id and id=(obj->>'id')::uuid;
  end loop;
 end loop;
 if (select count(*) from public.music_media where owner_id=p_owner)>100000 or
    (select count(*) from public.music_playlists where owner_id=p_owner)>5002 or
    (select count(*) from public.music_playlist_items where owner_id=p_owner)>405000 then raise exception 'library_too_large' using errcode='22023';end if;
end $$;
create function music_private.v2_reply(p_owner uuid,p_writer uuid,p_revision bigint,p_hash text,p_snapshot boolean) returns jsonb language plpgsql set search_path='' as $$
declare head public.music_sync_state;ack bigint;events jsonb;snap jsonb;first_hash text;cnt bigint;bytes bigint;
begin
 select * into head from public.music_sync_state where owner_id=p_owner;
 select last_sequence into ack from public.music_sync_writers where owner_id=p_owner and writer_id=p_writer;
 if not p_snapshot and p_revision=head.revision and p_hash=head.hash then
  return jsonb_build_object('mode','unchanged','revision',head.revision,'hash',head.hash,'document',null,'changes','[]'::jsonb,'acknowledged',coalesce(ack,0));end if;
 select count(*),coalesce(sum(pg_column_size(patch)),0) into cnt,bytes from public.music_sync_changes where owner_id=p_owner and revision>p_revision;
 select previous_hash into first_hash from public.music_sync_changes where owner_id=p_owner and revision=p_revision+1;
 if not p_snapshot and p_revision>=head.min_revision and p_revision<head.revision and cnt=head.revision-p_revision and cnt<=128 and bytes<524288 and first_hash=p_hash then
  select jsonb_agg(jsonb_build_object('revision',revision,'previousHash',previous_hash,'hash',hash,'patch',patch) order by revision) into events from public.music_sync_changes where owner_id=p_owner and revision>p_revision;
  if bytes>8192 then snap:=music_private.v2_snapshot(p_owner);end if;
  if snap is null or bytes<pg_column_size(snap) then
   return jsonb_build_object('mode','delta','revision',head.revision,'hash',head.hash,'document',null,'changes',events,'acknowledged',coalesce(ack,0));end if;
 end if;
 return jsonb_build_object('mode','snapshot','revision',head.revision,'hash',head.hash,'document',coalesce(snap,music_private.v2_snapshot(p_owner)),'changes','[]'::jsonb,'acknowledged',coalesce(ack,0));
end $$;
create function music_private.v2_pull(p_writer uuid,p_revision bigint,p_hash text,p_snapshot boolean) returns jsonb language plpgsql security definer set search_path='' as $$
declare actor uuid:=auth.uid();
begin
 if actor is null or p_writer is null then raise exception 'authentication_required' using errcode='42501';end if;
 perform pg_advisory_xact_lock(hashtextextended('music-v2:'||actor::text,0));
 perform music_private.v2_init(actor);
 insert into public.music_sync_writers values(actor,p_writer,0) on conflict do nothing;
 return music_private.v2_reply(actor,p_writer,p_revision,p_hash,p_snapshot);
end $$;
create function music_private.v2_submit(p_writer uuid,p_revision bigint,p_hash text,p_operations jsonb) returns jsonb language plpgsql security definer set search_path='' as $$
declare actor uuid:=auth.uid();head public.music_sync_state;last_seq bigint;c jsonb;before_doc jsonb;after_doc jsonb;prev_hash text;next_hash text;seq bigint;next_revision bigint;
begin
 if actor is null or p_writer is null then raise exception 'authentication_required' using errcode='42501';end if;
 if jsonb_typeof(p_operations)<>'array' or jsonb_array_length(p_operations)>128 or pg_column_size(p_operations)>1048576 then raise exception 'invalid_batch' using errcode='22023';end if;
 perform pg_advisory_xact_lock(hashtextextended('music-v2:'||actor::text,0));
 perform music_private.v2_init(actor);
 insert into public.music_sync_writers values(actor,p_writer,0) on conflict do nothing;
 select * into head from public.music_sync_state where owner_id=actor;
 if head.revision<>p_revision or head.hash<>p_hash then return music_private.v2_reply(actor,p_writer,p_revision,p_hash,false);end if;
 select last_sequence into last_seq from public.music_sync_writers where owner_id=actor and writer_id=p_writer;
 prev_hash:=head.hash;next_revision:=head.revision;
 for c in select value from jsonb_array_elements(p_operations) loop
  seq:=(c->>'sequence')::bigint;
  if seq<=last_seq then continue;end if;
  if seq<>last_seq+1 or (c->>'id')::uuid is null then raise exception 'operation_sequence_gap' using errcode='22023';end if;
  before_doc:=music_private.v2_snapshot(actor);next_revision:=next_revision+1;
  perform music_private.v2_apply(actor,c,next_revision);
  after_doc:=music_private.v2_snapshot(actor);next_hash:=music_private.v2_hash(actor);
  insert into public.music_sync_changes values(actor,next_revision,prev_hash,next_hash,music_private.v2_diff(before_doc,after_doc));
  prev_hash:=next_hash;last_seq:=seq;
 end loop;
 update public.music_sync_state set revision=next_revision,hash=prev_hash,min_revision=greatest(min_revision,next_revision-512) where owner_id=actor;
 update public.music_sync_writers set last_sequence=last_seq where owner_id=actor and writer_id=p_writer;
 delete from public.music_sync_changes where owner_id=actor and revision<=next_revision-512;
 return music_private.v2_reply(actor,p_writer,p_revision,p_hash,false);
end $$;
-- One-time conversion of the three development favorites. No old-reference compatibility.
do $$ declare old record;m jsonb;l jsonb;idx bigint;mid uuid;begin
 lock table public.music_libraries in share row exclusive mode;
 for old in select * from public.music_libraries loop
  if jsonb_array_length(old.document->'nodes')<>0 or jsonb_array_length(old.document->'entries')<>0 then raise exception 'unexpected_development_playlists';end if;
  perform music_private.v2_init(old.user_id);
  for m in select value from jsonb_array_elements(old.document->'media') loop
   insert into public.music_media values(old.user_id,(m->>'id')::uuid,m->>'bvid',(m->>'cid')::bigint,m->>'title',m->>'artist',m->>'cover',(m->>'durationSeconds')::bigint,m->>'duration');
  end loop;
  for l,idx in select value,ordinality from jsonb_array_elements(old.document->'likes') with ordinality loop
   mid:=(l->>'mediaId')::uuid;
   insert into public.music_playlist_items values(old.user_id,'00000000-0000-0000-0000-000000000002',mid,mid,null,'',floor(extract(epoch from (l->>'addedAt')::timestamptz)*1000)::bigint,idx);
  end loop;
  update public.music_sync_state set revision=old.revision,min_revision=old.revision,hash=music_private.v2_hash(old.user_id) where owner_id=old.user_id;
 end loop;
end $$;
create or replace function public.save_music_library(expected_revision bigint,library_document jsonb) returns jsonb language plpgsql security invoker set search_path='' as $$
begin raise exception 'library_upgrade_required' using errcode='0A000';end $$;
create function public.music_pull_v2(p_writer uuid,p_revision bigint,p_hash text,p_snapshot boolean default false) returns jsonb language sql security invoker set search_path='' as $$
 select music_private.v2_pull(p_writer,p_revision,p_hash,p_snapshot);
$$;
create function public.music_submit_v2(p_writer uuid,p_revision bigint,p_hash text,p_operations jsonb) returns jsonb language sql security invoker set search_path='' as $$
 select music_private.v2_submit(p_writer,p_revision,p_hash,p_operations);
$$;
revoke all on all functions in schema music_private from public,anon,authenticated;
grant execute on function music_private.v2_pull(uuid,bigint,text,boolean),music_private.v2_submit(uuid,bigint,text,jsonb) to authenticated;
revoke all on function public.music_pull_v2(uuid,bigint,text,boolean),public.music_submit_v2(uuid,bigint,text,jsonb) from public,anon,authenticated;
grant execute on function public.music_pull_v2(uuid,bigint,text,boolean),public.music_submit_v2(uuid,bigint,text,jsonb) to authenticated;
