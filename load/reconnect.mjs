// Run from tools/browser-checks after npm ci. Opens 200 browser-side SignalR viewers and forces one reconnect storm.
import {createRequire} from 'node:module';
const require=createRequire(new URL('../tools/browser-checks/package.json',import.meta.url));
const {HubConnectionBuilder,LogLevel}=require('@microsoft/signalr');
const base=process.env.BASE_URL,slug=process.env.VENUE_SLUG,date=process.env.LOCAL_DATE;
if(!base||!slug||!date)throw new Error('Set BASE_URL, VENUE_SLUG, LOCAL_DATE');
const connections=Array.from({length:200},()=>new HubConnectionBuilder().withUrl(`${base}/hubs/availability`).withAutomaticReconnect().configureLogging(LogLevel.Error).build());
const latencies=[];let published=0;
for(const connection of connections){for(const event of ['SlotHeld','SlotBooked'])connection.on(event,p=>{if(published&&p.courtId===process.env.COURT_ID)latencies.push(Date.now()-published);});}
await Promise.all(connections.map(async c=>{await c.start();await c.invoke('JoinVenueDate',slug,date);}));
if(!process.env.PLAYER_TOKEN||!process.env.COURT_ID||!process.env.START_AT||!process.env.END_AT)throw new Error('Also set PLAYER_TOKEN, COURT_ID, START_AT, END_AT');
async function hold(){published=Date.now();const response=await fetch(`${base}/api/v1/bookings/holds`,{method:'POST',headers:{Authorization:`Bearer ${process.env.PLAYER_TOKEN}`,'Content-Type':'application/json','Idempotency-Key':crypto.randomUUID()},body:JSON.stringify({courtId:process.env.COURT_ID,startAt:process.env.START_AT,endAt:process.env.END_AT})});if(response.status!==201)throw new Error(`Hold failed: ${response.status}`);return (await response.json()).id;}
async function release(id){await fetch(`${base}/api/v1/bookings/${id}/release`,{method:'POST',headers:{Authorization:`Bearer ${process.env.PLAYER_TOKEN}`}});published=0;}
const initial=await hold();await new Promise(r=>setTimeout(r,1500));await release(initial);
console.log('200 viewers connected. Restart the database now if running the isolated failure drill.');
await new Promise(r=>setTimeout(r,30000));
await Promise.all(connections.map(c=>c.stop()));
await Promise.all(connections.map(async c=>{await c.start();await c.invoke('JoinVenueDate',slug,date);}));
const after=await hold();await new Promise(r=>setTimeout(r,1500));await release(after);await Promise.all(connections.map(c=>c.stop()));
if(latencies.length){const p95=latencies.sort((a,b)=>a-b)[Math.floor(latencies.length*.95)];console.log(`SignalR p95 ${p95}ms (${latencies.length} events)`);if(p95>=1000)process.exitCode=1;}else{console.error('No timestamped booking events captured; delivery target NOT VERIFIED.');process.exitCode=2;}
