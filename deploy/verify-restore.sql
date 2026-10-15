SELECT 'bookings' AS resource,count(*) AS rows,max(start_at) AS latest_booking FROM bookings;
SELECT 'payments' AS resource,count(*) AS rows,sum(amount) AS signed_amount FROM payments;
SELECT 'customers' AS resource,count(*) AS rows FROM customers;
SELECT count(*) AS overlaps FROM bookings a JOIN bookings b ON a.id<b.id AND a.court_id=b.court_id AND a.status NOT IN ('CANCELLED','EXPIRED') AND b.status NOT IN ('CANCELLED','EXPIRED') AND tstzrange(a.start_at,a.end_at,'[)') && tstzrange(b.start_at,b.end_at,'[)');
SELECT count(*) AS migrations FROM "__EFMigrationsHistory";
