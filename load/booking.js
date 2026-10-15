import http from 'k6/http';
import {check,sleep,fail} from 'k6';
import {Trend,Counter} from 'k6/metrics';
const fixture=JSON.parse(open(__ENV.FIXTURE||'fixture.json'));
const base=__ENV.BASE_URL;
if(!base)throw new Error('BASE_URL is required; use an isolated staging database.');
const availability=new Trend('availability_ms',true);
const accepted=new Counter('race_accepted');
export const options={scenarios:{viewers:{executor:'constant-vus',vus:200,duration:'10m',exec:'view'},race:{executor:'per-vu-iterations',vus:50,iterations:1,startTime:'10s',exec:'race'},mixed:{executor:'constant-vus',vus:20,duration:'10m',exec:'mixed'}},thresholds:{availability_ms:['p(95)<300'],checks:['rate>0.99'],race_accepted:['count==1']}};
function availabilityRequest(){const r=http.get(`${base}/api/v1/public/venues/${fixture.slug}/availability?date=${fixture.date}`);availability.add(r.timings.duration);check(r,{'availability succeeds':x=>x.status===200});}
export function view(){availabilityRequest();sleep(25+Math.random()*5);}
export function race(){const token=fixture.players[(__VU-1)%fixture.players.length];const r=http.post(`${base}/api/v1/bookings/holds`,JSON.stringify({courtId:fixture.courtId,startAt:fixture.startAt,endAt:fixture.endAt}),{headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json','Idempotency-Key':`race-${__VU}-${fixture.runId}`}});if(r.status===201)accepted.add(1);check(r,{'one hold or conflict':x=>x.status===201||x.status===409});}
export function mixed(){availabilityRequest();const token=fixture.players[(__VU-1)%fixture.players.length];const r=http.post(`${base}/api/v1/bookings/holds`,JSON.stringify({courtId:fixture.mixedCourtId,startAt:fixture.startAt,endAt:fixture.endAt}),{headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json','Idempotency-Key':`mixed-${__VU}-${__ITER}-${fixture.runId}`}});if(r.status===201){const id=r.json('id');http.post(`${base}/api/v1/bookings/${id}/release`,null,{headers:{Authorization:`Bearer ${token}`}});}check(r,{'mixed result':x=>x.status===201||x.status===409});sleep(30);}
