-- Occurrence time comes from the device. Receipt time is server metadata.
-- Pre-migration rows have unknown receipt times; do not manufacture old timestamps.
alter table public.music_playlist_items add column received_at timestamptz;
alter table public.music_playlist_items alter column received_at set default clock_timestamp();
comment on column public.music_playlist_items.received_at is 'Server receipt time; distinct from device added_at_ms. NULL for older records.';
alter table public.music_sync_changes add column received_at timestamptz;
alter table public.music_sync_changes alter column received_at set default clock_timestamp();
create or replace function music_private.v2_submit(p_writer uuid,p_revision bigint,p_hash text,p_operations jsonb) returns jsonb language plpgsql security definer set search_path='' as $$
declare actor uuid:=auth.uid();head public.music_sync_state;last_seq bigint;c jsonb;before_doc jsonb;after_doc jsonb;prev_hash text;next_hash text;seq bigint;next_revision bigint;cutoff bigint;
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
 -- Bound retained bytes too: a large deletion need not occupy the entire log window.
 select max(revision) into cutoff from (
  select revision,sum(octet_length(patch::text)) over(order by revision desc) as bytes
  from public.music_sync_changes where owner_id=actor
 ) retained where bytes>8388608;
 if cutoff is not null then
  delete from public.music_sync_changes where owner_id=actor and revision<=cutoff;
  update public.music_sync_state set min_revision=greatest(min_revision,cutoff) where owner_id=actor;
 end if;
 return music_private.v2_reply(actor,p_writer,p_revision,p_hash,false);
end $$;
create or replace function music_private.v2_reply(p_owner uuid,p_writer uuid,p_revision bigint,p_hash text,p_snapshot boolean) returns jsonb language plpgsql set search_path='' as $$
declare head public.music_sync_state;ack bigint;events jsonb;snap jsonb;first_hash text;cnt bigint;bytes bigint;
begin
 select * into head from public.music_sync_state where owner_id=p_owner;
 select last_sequence into ack from public.music_sync_writers where owner_id=p_owner and writer_id=p_writer;
 if not p_snapshot and p_revision=head.revision and p_hash=head.hash then
  return jsonb_build_object('mode','unchanged','revision',head.revision,'hash',head.hash,'document',null,'changes','[]'::jsonb,'acknowledged',coalesce(ack,0));end if;
 select count(*),coalesce(sum(octet_length(patch::text)),0) into cnt,bytes from public.music_sync_changes where owner_id=p_owner and revision>p_revision;
 select previous_hash into first_hash from public.music_sync_changes where owner_id=p_owner and revision=p_revision+1;
 if not p_snapshot and p_revision>=head.min_revision and p_revision<head.revision and cnt=head.revision-p_revision and cnt<=128 and bytes<524288 and first_hash=p_hash then
  select jsonb_agg(jsonb_build_object('revision',revision,'previousHash',previous_hash,'hash',hash,'patch',patch) order by revision) into events from public.music_sync_changes where owner_id=p_owner and revision>p_revision;
  if bytes>8192 then snap:=music_private.v2_snapshot(p_owner);end if;
  if snap is null or bytes<octet_length(snap::text) then
   return jsonb_build_object('mode','delta','revision',head.revision,'hash',head.hash,'document',null,'changes',events,'acknowledged',coalesce(ack,0));end if;
 end if;
 return jsonb_build_object('mode','snapshot','revision',head.revision,'hash',head.hash,'document',coalesce(snap,music_private.v2_snapshot(p_owner)),'changes','[]'::jsonb,'acknowledged',coalesce(ack,0));
end $$;
