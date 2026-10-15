-- Must return zero overlaps. Run after every load/failure test in the isolated database.
SELECT count(*) AS overlapping_active_bookings FROM bookings a JOIN bookings b ON a.id<b.id AND a.court_id=b.court_id AND a.status NOT IN ('CANCELLED','EXPIRED') AND b.status NOT IN ('CANCELLED','EXPIRED') AND tstzrange(a.start_at,a.end_at,'[)') && tstzrange(b.start_at,b.end_at,'[)');
SELECT court_id,start_at,end_at,count(*) FROM bookings WHERE status NOT IN ('CANCELLED','EXPIRED') GROUP BY court_id,start_at,end_at HAVING count(*)>1;
