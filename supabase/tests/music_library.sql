-- All fixtures are rolled back. This does not send emails or create Auth sessions.
begin;
insert into auth.users(id,aud,role,email,created_at,updated_at) values
('10000000-0000-4000-8000-000000000001','authenticated','authenticated','library-a@example.invalid',now(),now()),
('10000000-0000-4000-8000-000000000002','authenticated','authenticated','library-b@example.invalid',now(),now());
set local role authenticated;
select set_config('request.jwt.claim.sub','10000000-0000-4000-8000-000000000001',true);
do $$
declare
 writer uuid:=gen_random_uuid();writer_b uuid:=gen_random_uuid();
 playlist uuid:=gen_random_uuid();child uuid:=gen_random_uuid();media uuid:=gen_random_uuid();
 head jsonb;old_head jsonb;ops jsonb;result jsonb;doc jsonb;favorite jsonb;
begin
 head:=public.music_pull_v2(writer,0,'',false);
 if head->>'mode'<>'snapshot' or (head->>'revision')::bigint<>0 then raise exception 'new library not empty';end if;
 ops:=jsonb_build_array(
  jsonb_build_object('type','remember','media',jsonb_build_object('id',media,'bvid','BV_fixture','cid',1,'title','中文测试','artist','','cover','','durationSeconds',1,'duration','0:01')),
  jsonb_build_object('type','create','nodeId',playlist,'name','父歌单'),
  jsonb_build_object('type','create','nodeId',child,'parentId',playlist,'name','子歌单'),
  jsonb_build_object('type','add_tracks','parentId',child,'tracks',jsonb_build_array(jsonb_build_object('bvid','BV_fixture','cid',1,'entryId',gen_random_uuid()))),
  jsonb_build_object('type','like','bvid','BV_fixture','cid',1,'liked',true,'occurredAt',1234567890123));
 select jsonb_agg(value||jsonb_build_object('id',gen_random_uuid(),'sequence',ordinality)) into ops from jsonb_array_elements(ops) with ordinality;
 old_head:=head;
 head:=public.music_submit_v2(writer,(head->>'revision')::bigint,head->>'hash',ops);
 if (head->>'revision')::bigint<>5 or (head->>'acknowledged')::bigint<>5 then raise exception 'initial batch failed';end if;
 result:=public.music_submit_v2(writer,0,old_head->>'hash',ops);
 if result->>'revision'<>head->>'revision' then raise exception 'stale submission changed data';end if;
 result:=public.music_submit_v2(writer,5,head->>'hash',ops);
 if result->>'revision'<>head->>'revision' then raise exception 'duplicate submission changed data';end if;
 doc:=public.music_pull_v2(writer,0,'',true)->'document';
 favorite:=doc->'likes'->0;
 if jsonb_array_length(doc->'nodes')<>2 or jsonb_array_length(doc->'entries')<>1 then raise exception 'mixed tree missing';end if;
 ops:=jsonb_build_array(jsonb_build_object('id',gen_random_uuid(),'sequence',6,'type','like','bvid','BV_fixture','cid',1,'liked',true,'occurredAt',1234567899999));
 head:=public.music_submit_v2(writer,5,head->>'hash',ops);
 doc:=public.music_pull_v2(writer,0,'',true)->'document';
 if doc->'likes'->0<>favorite then raise exception 'duplicate favorite changed timeline';end if;
 -- B+ first; A has queued an independent A+ and A-. Do not cancel the pair.
 result:=public.music_submit_v2(writer_b,6,head->>'hash',jsonb_build_array(jsonb_build_object('id',gen_random_uuid(),'sequence',1,'type','add_tracks','parentId',child,'tracks',jsonb_build_array(jsonb_build_object('bvid','BV_fixture','cid',1,'entryId',gen_random_uuid())))));
 head:=public.music_pull_v2(writer,6,head->>'hash',false);
 ops:=jsonb_build_array(
  jsonb_build_object('id',gen_random_uuid(),'sequence',7,'type','add_tracks','parentId',child,'tracks',jsonb_build_array(jsonb_build_object('bvid','BV_fixture','cid',1,'entryId',gen_random_uuid()))),
  jsonb_build_object('id',gen_random_uuid(),'sequence',8,'type','remove_track','parentId',child,'bvid','BV_fixture','cid',1));
 head:=public.music_submit_v2(writer,(head->>'revision')::bigint,head->>'hash',ops);
 if exists(select 1 from public.music_playlist_items where parent_id=child and media_id=media) then raise exception 'ABA cancellation used the wrong identity';end if;
 ops:=jsonb_build_array(
  jsonb_build_object('id',gen_random_uuid(),'sequence',9,'type','delete','nodeId',playlist),
  jsonb_build_object('id',gen_random_uuid(),'sequence',10,'type','add_tracks','parentId',child,'tracks',jsonb_build_array(jsonb_build_object('bvid','BV_fixture','cid',1,'entryId',gen_random_uuid()))));
 head:=public.music_submit_v2(writer,(head->>'revision')::bigint,head->>'hash',ops);
 doc:=public.music_pull_v2(writer,0,'',true)->'document';
 if jsonb_array_length(doc->'nodes')<>0 or jsonb_array_length(doc->'entries')<>0 or jsonb_array_length(doc->'likes')<>1 then raise exception 'subtree delete/replay incorrect';end if;
 begin
  delete from public.music_media;
  raise exception 'direct writes allowed';
 exception when insufficient_privilege then null;end;
 begin
  perform music_private.v2_apply(auth.uid(),'{}',1);
  raise exception 'private mutation helper exposed';
 exception when insufficient_privilege then null;end;
 if public.music_pull_v2(writer,(head->>'revision')::bigint,head->>'hash',false)->>'mode'<>'unchanged' then raise exception 'unchanged poll sends data';end if;
end $$;
select set_config('request.jwt.claim.sub','10000000-0000-4000-8000-000000000002',true);
do $$ declare result jsonb;begin
 if exists(select 1 from public.music_media) then raise exception 'cross-account RLS leak';end if;
 result:=public.music_pull_v2(gen_random_uuid(),0,'',false);
 if jsonb_array_length(result->'document'->'media')<>0 then raise exception 'cross-account RPC leak';end if;
end $$;
reset role;
do $$ begin
 if has_function_privilege('anon','public.music_pull_v2(uuid,bigint,text,boolean)','execute') or has_function_privilege('anon','public.music_submit_v2(uuid,bigint,text,jsonb)','execute') then raise exception 'anonymous RPC access';end if;
 if has_table_privilege('anon','public.music_media','select') then raise exception 'anonymous table access';end if;
 if exists(select 1 from public.music_sync_state where owner_id in('10000000-0000-4000-8000-000000000001','10000000-0000-4000-8000-000000000002') and hash<>music_private.v2_hash(owner_id)) then raise exception 'server hash diverged';end if;
end $$;
select 'PASS: relational replay, ABA, idempotence, recursive deletion, timeline and account isolation' result;
rollback;
