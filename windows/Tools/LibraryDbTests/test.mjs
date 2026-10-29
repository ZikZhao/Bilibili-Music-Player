import { PGlite } from '@electric-sql/pglite';
import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';

const db = new PGlite();
let nextId = 1;
function randomUUID(){return `90000000-0000-4000-8000-${String(nextId++).padStart(12,'0')}`;}
const owner = randomUUID(), other = randomUUID(), writer = randomUUID(), writerB = randomUUID();
const root = '00000000-0000-0000-0000-000000000001';
const favorites = '00000000-0000-0000-0000-000000000002';
const media = (key) => ({id:randomUUID(),bvid:'BV'+key,cid:1,title:'中文 '+key,artist:'歌手',cover:'https://example.test/'+key,durationSeconds:120,duration:'02:00'});
const oldMedia = [media('one'),media('two'),media('three')];
const legacy = {version:1,media:oldMedia,nodes:[],entries:[],likes:oldMedia.map((m,i)=>({mediaId:m.id,addedAt:`2026-10-${20+i}T10:00:00.123Z`}))};
await db.exec(`create role anon; create role authenticated; create role service_role;
 create schema auth; create table auth.users(id uuid primary key);
 create function auth.uid() returns uuid language sql stable as $$select nullif(current_setting('request.jwt.claim.sub',true),'')::uuid$$;
 grant usage on schema auth to authenticated,anon;
 create schema music_private; grant usage on schema music_private to authenticated;
 create table public.music_libraries(user_id uuid primary key,revision bigint,document jsonb);`);
await db.query('insert into auth.users values($1),($2)',[owner,other]);
await db.query('insert into public.music_libraries values($1,4,$2)',[owner,JSON.stringify(legacy)]);
await db.exec(readFileSync(new URL('../../../supabase/migrations/20261029095524_relational_library_sync.sql',import.meta.url),'utf8'));
await db.exec(readFileSync(new URL('../../../supabase/migrations/20261029100103_library_receipt_timestamps.sql',import.meta.url),'utf8'));
let checks = 0;
function check(v,msg){assert.ok(v,msg);checks++;}
async function scalar(sql,args=[]){return (await db.query(sql,args)).rows[0].result;}
async function login(id=owner){await db.exec('reset role; set role authenticated');await db.query("select set_config('request.jwt.claim.sub',$1,false)",[id]);}
async function pull(w=writer,rev=0,hash='',snapshot=false){return scalar('select public.music_pull_v2($1,$2,$3,$4) result',[w,rev,hash,snapshot]);}
let seq=0, head;
async function submit(commands,w=writer,base=head){
 const ops=commands.map(c=>({id:randomUUID(),sequence:++seq,...c}));
 return scalar('select public.music_submit_v2($1,$2,$3,$4) result',[w,base.revision,base.hash,JSON.stringify(ops)]);
}
async function snapshot(){return (await pull(writer,0,'',true)).document;}
function hash(d){
 const b=s=>Buffer.from(s??'','utf8').toString('base64');const sorted=(a,k='id')=>[...a].sort((x,y)=>x[k].localeCompare(y[k]));
 let s='library-v2\n';
 for(const m of sorted(d.media))s+=`m|${m.id}|${b(m.bvid)}|${m.cid}|${b(m.title)}|${b(m.artist)}|${b(m.cover)}|${m.durationSeconds}|${b(m.duration)}\n`;
 for(const n of sorted(d.nodes))s+=`n|${n.id}|${b(n.name)}|${n.parentId??'-'}|${n.rank}\n`;
 for(const e of sorted(d.entries))s+=`e|${e.id}|${e.playlistId}|${e.mediaId}|${e.rank}\n`;
 for(const l of sorted(d.likes,'mediaId'))s+=`l|${l.mediaId}|${Date.parse(l.addedAt)}|${l.sequence}\n`;
 return createHash('sha256').update(s).digest('hex');
}
await login();head=await pull();check(head.mode==='snapshot'&&head.revision===4,'legacy converted once');
check(head.document.likes.length===3&&head.document.likes.every(l=>legacy.likes.some(o=>o.mediaId===l.mediaId&&o.addedAt===l.addedAt)),'favorite identity and occurrence time preserved');
check(head.hash===hash(head.document),'SQL and canonical UTF8 hash agree');
check((await pull(writer,head.revision,head.hash)).mode==='unchanged','no-op poll is metadata only');
await assert.rejects(()=>db.exec(`delete from public.music_media`));checks++;
await assert.rejects(()=>db.query('select music_private.v2_apply($1,$2,1)',[owner,'{}']));checks++;
await login(other);check((await pull()).document.media.length===0,'other account starts empty');
check((await db.query('select * from public.music_media')).rows.length===0,'RLS isolates reads');
await login();
const a=media('A'), p=randomUUID(), child=randomUUID();
head=await submit([{type:'remember',media:a},{type:'create',nodeId:p,name:'父歌单'},{type:'add_tracks',parentId:p,tracks:[{bvid:a.bvid,cid:a.cid,entryId:randomUUID()}]},{type:'create',nodeId:child,parentId:p,name:'子歌单'}]);
let d=await snapshot();check(d.nodes.length===2&&d.entries.length===1,'mixed songs and child playlists');
check(d.entries[0].rank<d.nodes.find(n=>n.id===child).rank,'one shared rank sequence');
check(head.hash===hash(d),'mixed hierarchy hash');
head=await submit([{type:'move_node',nodeId:p,parentId:child}]);d=await snapshot();check(d.nodes.find(n=>n.id===p).parentId===null,'cycle is a safe no-op');
head=await submit([{type:'like',bvid:a.bvid,cid:1,liked:true,occurredAt:1234567890123}]);
const first=(await snapshot()).likes.find(l=>l.mediaId===a.id);
head=await submit([{type:'like',bvid:a.bvid,cid:1,liked:true,occurredAt:1234567899999}]);
check(JSON.stringify((await snapshot()).likes.find(l=>l.mediaId===a.id))===JSON.stringify(first),'duplicate favorite keeps time and tie-break');
// B+ commits first. Both of A's locally queued intents must survive batching.
const bBase=await pull(writerB,head.revision,head.hash);
const bResult=await scalar('select public.music_submit_v2($1,$2,$3,$4) result',[writerB,bBase.revision,bBase.hash,JSON.stringify([{id:randomUUID(),sequence:1,type:'add_tracks',parentId:child,tracks:[{bvid:a.bvid,cid:1,entryId:randomUUID()}]}])]);
const aBase=head;
const queued=[{id:randomUUID(),sequence:seq+1,type:'add_tracks',parentId:child,tracks:[{bvid:a.bvid,cid:1,entryId:randomUUID()}]},{id:randomUUID(),sequence:seq+2,type:'remove_track',parentId:child,bvid:a.bvid,cid:1}];
const stale=await scalar('select public.music_submit_v2($1,$2,$3,$4) result',[writer,aBase.revision,aBase.hash,JSON.stringify(queued)]);
check(stale.acknowledged===seq&&stale.revision===bResult.revision,'stale base pulls before applying intents');
head=await scalar('select public.music_submit_v2($1,$2,$3,$4) result',[writer,stale.revision,stale.hash,JSON.stringify(queued)]);seq+=2;
check(!(await snapshot()).entries.some(e=>e.playlistId===child),'B+ A+ A- removes membership by song, independent of entry id');
const retry=await scalar('select public.music_submit_v2($1,$2,$3,$4) result',[writer,head.revision,head.hash,JSON.stringify(queued)]);
check(retry.revision===head.revision&&retry.acknowledged===seq,'lost acknowledgement retry does not replay');
// A real mixed relational snapshot checks cross-language hashing and JSON decoding.
const parity={hash:head.hash,document:await snapshot(),ranks:[]};
head=await submit([{type:'delete',nodeId:p},{type:'add_tracks',parentId:child,tracks:[{bvid:a.bvid,cid:1,entryId:randomUUID()}]}]);
d=await snapshot();check(d.nodes.length===0&&d.entries.length===0&&d.likes.some(l=>l.mediaId===a.id),'recursive deletion wins, metadata and default favorite survive');
head=await submit([{type:'create',nodeId:randomUUID(),parentId:favorites,name:'invalid child'}]);
check((await snapshot()).nodes.length===0,'default playlist cannot contain children');
const before=head, oldSeq=seq;
await assert.rejects(()=>submit([{type:'rename',nodeId:p,name:'x'},{type:'bogus'}]));seq=oldSeq;
check((await pull(writer,before.revision,before.hash)).revision===before.revision,'invalid batch rolls back atomically');
const emptyHash=hash({media:[],nodes:[],entries:[],likes:[]});
await db.exec('reset role');
check(await scalar("select music_private.rank_between(null,null) result")==='a0','initial rank');
let last=null;
for(let i=0;i<100;i++){const r=await scalar('select music_private.rank_between($1,null) result',[last]);check(last===null||last<r,'append rank order');parity.ranks.push({a:last,b:null,result:r});last=r;}
let left='a0',right='a1';
for(let i=0;i<30;i++){const r=await scalar('select music_private.rank_between($1,$2) result',[left,right]);check(left<r&&r<right,'dense midpoint order');parity.ranks.push({a:left,b:right,result:r});right=r;}
last='a0';
for(let i=0;i<100;i++){const r=await scalar('select music_private.rank_between(null,$1) result',[last]);check(r<last,'prepend rank order');parity.ranks.push({a:null,b:last,result:r});last=r;}
writeFileSync(new URL('parity-fixture.json',import.meta.url),JSON.stringify(parity,null,2)+'\n');
await login();
for(let batch=0;batch<5;batch++)head=await submit(Array.from({length:110},()=>({type:'rename',nodeId:p,name:'deleted target'})));
const long=await pull(writer,4,emptyHash);check(long.mode==='snapshot','old cursor skips compacted log');
await db.exec('reset role');
check(Number(await scalar('select count(*) result from public.music_sync_changes where owner_id=$1',[owner]))===512,'log retention bounded');
check(await scalar('select received_at is not null result from public.music_playlist_items where owner_id=$1 and media_id=$2',[owner,a.id]),'server receipt time exists separately');
check(await scalar('select received_at is null result from public.music_playlist_items where owner_id=$1 and media_id=$2',[owner,oldMedia[0].id]),'historical receipt time remains unknown');
await db.query("update public.music_sync_changes set patch=jsonb_build_object('padding',repeat('x',9000000)) where owner_id=$1 and revision=$2",[owner,head.revision]);
await login();
check((await pull(writer,head.revision-1,head.hash)).mode==='snapshot','wire size uses expanded bytes even when TOAST compresses the log');
head=await submit([{type:'rename',nodeId:p,name:'deleted'}]);
await db.exec('reset role');
check(Number(await scalar('select count(*) result from public.music_sync_changes where owner_id=$1',[owner]))===1,'large patches cannot retain an unbounded byte history');
await login();
await db.exec("select set_config('request.jwt.claim.sub','',false)");
await assert.rejects(()=>pull());checks++;
await db.close();
console.log(`${checks} PostgreSQL protocol checks passed`);
