using Futsal.Application.Reports;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Infrastructure.Reports;

public sealed class ReportData
{
    public string Category { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
    public int Count { get; set; }
    public decimal Value { get; set; }
    public string? Detail { get; set; }
}
public sealed class ReportQueries(FutsalDbContext db)
{
    public async Task<List<ReportData>> ExecuteAsync(string kind, Guid org, Guid venue, DateOnly from, DateOnly to, string zone, Guid? cashier, CancellationToken ct, DateTimeOffset? shiftStart = null, DateTimeOffset? shiftEnd = null)
    {
        var (start, end) = ReportRules.UtcRange(from, to, zone);
        if (kind == "shift" && shiftStart != null && shiftEnd != null) { start = shiftStart.Value.ToUniversalTime(); end = shiftEnd.Value.ToUniversalTime(); }
        var previousEnd = ReportRules.UtcRange(to.AddDays(1).AddMonths(-1), to.AddDays(1).AddMonths(-1), zone).Start;
        var previousStart = ReportRules.UtcRange(from.AddMonths(-1), from.AddDays(-1), zone).Start;
        // All identifiers and fragments are selected from constants. Every user value is an SQL parameter.
        string paymentCte = """
   WITH pay AS (
    SELECT p.*, (COALESCE(p.verified_at,p.created_at) AT TIME ZONE @zone)::date AS local_day
    FROM payments p JOIN bookings b ON b.id=p.booking_id AND b.organization_id=p.organization_id
    WHERE p.organization_id=@org AND b.venue_id=@venue AND p.status IN ('COMPLETED','REFUNDED')
     AND COALESCE(p.verified_at,p.created_at)>=@start AND COALESCE(p.verified_at,p.created_at)<@end
     AND (@cashier::uuid IS NULL OR p.received_by=@cashier)
   ), slots AS (
    SELECT * FROM bookings WHERE organization_id=@org AND venue_id=@venue AND start_at>=@start AND start_at<@end
   )
   """;
        string sql = kind switch
        {
            "daily" => paymentCte + """
    SELECT 'income' AS "Category",method::text AS "Label",SUM(amount) AS "Amount",COUNT(*)::int AS "Count",0::numeric AS "Value",NULL::text AS "Detail" FROM pay GROUP BY method
    UNION ALL SELECT 'channel', CASE WHEN method='CASH' THEN 'CASH' WHEN method IN ('ESEWA','KHALTI','FONEPAY') THEN 'ONLINE' ELSE 'OTHER' END,SUM(amount),COUNT(*)::int,0,NULL FROM pay GROUP BY 2
    UNION ALL SELECT 'bookings',status::text,SUM(total_amount),COUNT(*)::int,0,NULL FROM slots GROUP BY status
    UNION ALL SELECT 'dues','created',COALESCE(SUM(GREATEST(total_amount-paid_amount,0)),0),COUNT(*)::int,0,NULL FROM bookings WHERE organization_id=@org AND venue_id=@venue AND created_at>=@start AND created_at<@end AND status IN ('CONFIRMED','COMPLETED','NO_SHOW')
    """,
            "monthly" => paymentCte + """
    SELECT 'income' AS "Category",local_day::text AS "Label",SUM(amount) AS "Amount",COUNT(*)::int AS "Count",0::numeric AS "Value",NULL::text AS "Detail" FROM pay GROUP BY local_day
    UNION ALL SELECT 'method',method::text,SUM(amount),COUNT(*)::int,0,NULL FROM pay GROUP BY method
    UNION ALL SELECT 'bookings','total',COALESCE(SUM(total_amount),0),COUNT(*)::int,COALESCE(AVG(total_amount),0),NULL FROM slots WHERE status IN ('CONFIRMED','COMPLETED','NO_SHOW')
    UNION ALL SELECT 'comparison','previous',COALESCE(SUM(p.amount),0),COUNT(*)::int,0,NULL FROM payments p JOIN bookings b ON b.id=p.booking_id AND b.organization_id=p.organization_id WHERE p.organization_id=@org AND b.venue_id=@venue AND p.status IN ('COMPLETED','REFUNDED') AND COALESCE(p.verified_at,p.created_at)>=@previous AND COALESCE(p.verified_at,p.created_at)<@previousEnd
    """,
            "shift" => paymentCte + """
    SELECT 'shift' AS "Category",method::text AS "Label",SUM(amount) AS "Amount",COUNT(*)::int AS "Count",0::numeric AS "Value",NULL::text AS "Detail" FROM pay GROUP BY method
    """,
            "dues" => """
    SELECT 'dues' AS "Category",c.full_name AS "Label",SUM(GREATEST(b.total_amount-b.paid_amount,0)) AS "Amount",COUNT(*)::int AS "Count",GREATEST(EXTRACT(DAY FROM @end-MIN(b.start_at)),0)::numeric AS "Value",
    (SELECT (MAX(n.sent_at) AT TIME ZONE @zone)::text FROM notifications n WHERE n.organization_id=@org AND n.venue_id=@venue AND n.customer_id=c.id AND n.status='SENT' AND n.template='due') AS "Detail"
    FROM bookings b JOIN customers c ON c.id=b.customer_id AND c.organization_id=b.organization_id
    WHERE b.organization_id=@org AND b.venue_id=@venue AND b.start_at>=@start AND b.start_at<@end AND b.status IN ('CONFIRMED','COMPLETED','NO_SHOW') AND b.total_amount>b.paid_amount GROUP BY c.id,c.full_name
    """,
            "customers" => """
    WITH customers_bookings AS (
     SELECT customer_id,COUNT(*)::int AS bookings,COUNT(*) FILTER(WHERE status='NO_SHOW')::int AS no_shows FROM bookings WHERE organization_id=@org AND venue_id=@venue AND start_at>=@start AND start_at<@end AND status IN ('CONFIRMED','COMPLETED','NO_SHOW') GROUP BY customer_id
    ), spend AS (
     SELECT b.customer_id,SUM(p.amount) AS amount FROM payments p JOIN bookings b ON b.id=p.booking_id AND b.organization_id=p.organization_id WHERE p.organization_id=@org AND b.venue_id=@venue AND p.status IN ('COMPLETED','REFUNDED') AND COALESCE(p.verified_at,p.created_at)>=@start AND COALESCE(p.verified_at,p.created_at)<@end GROUP BY b.customer_id
    )
    SELECT 'customers' AS "Category",c.full_name AS "Label",COALESCE(s.amount,0) AS "Amount",COALESCE(b.bookings,0) AS "Count",COALESCE(b.no_shows,0)::numeric AS "Value",NULL::text AS "Detail" FROM customers c LEFT JOIN customers_bookings b ON b.customer_id=c.id LEFT JOIN spend s ON s.customer_id=c.id WHERE c.organization_id=@org AND (b.customer_id IS NOT NULL OR s.customer_id IS NOT NULL) ORDER BY "Amount" DESC,"Count" DESC
    """,
            "utilization" => """
    WITH days AS (SELECT generate_series((@start AT TIME ZONE @zone)::date,(@end AT TIME ZONE @zone)::date-1,interval '1 day')::date AS day),
    available AS (
     SELECT c.id,c.name,c.slot_minutes,d.day, g.local_start AT TIME ZONE @zone AS slot_start, (g.local_start+make_interval(mins=>c.slot_minutes)) AT TIME ZONE @zone AS slot_end
     FROM courts c CROSS JOIN days d JOIN court_hours h ON h.court_id=c.id AND h.weekday=EXTRACT(DOW FROM d.day)
     CROSS JOIN LATERAL generate_series(d.day+h.open_time,d.day+h.close_time-make_interval(mins=>c.slot_minutes),make_interval(mins=>c.slot_minutes)) g(local_start)
     WHERE c.organization_id=@org AND c.venue_id=@venue AND c.is_active AND NOT EXISTS(SELECT 1 FROM blocked_periods p WHERE p.organization_id=@org AND p.court_id=c.id AND p.start_at<(g.local_start+make_interval(mins=>c.slot_minutes)) AT TIME ZONE @zone AND p.end_at>g.local_start AT TIME ZONE @zone)
    ), occupied AS (
     SELECT a.*,EXISTS(SELECT 1 FROM bookings b WHERE b.organization_id=@org AND b.venue_id=@venue AND b.court_id=a.id AND b.status IN ('CONFIRMED','COMPLETED','NO_SHOW') AND b.start_at<a.slot_end AND b.end_at>a.slot_start) AS booked FROM available a
    )
    SELECT 'court' AS "Category",name AS "Label",0::numeric AS "Amount",COUNT(*)::int AS "Count",ROUND(100.0*COUNT(*) FILTER(WHERE booked)/NULLIF(COUNT(*),0),2) AS "Value",NULL::text AS "Detail" FROM occupied GROUP BY id,name
    UNION ALL SELECT 'day',day::text,0,COUNT(*) FILTER(WHERE booked)::int,ROUND(100.0*COUNT(*) FILTER(WHERE booked)/NULLIF(COUNT(*),0),2),NULL FROM occupied GROUP BY day
    UNION ALL SELECT 'busiest',day::text,0,booked_count,percentage,NULL FROM (SELECT day,COUNT(*) FILTER(WHERE booked)::int AS booked_count,ROUND(100.0*COUNT(*) FILTER(WHERE booked)/NULLIF(COUNT(*),0),2) AS percentage FROM occupied GROUP BY day ORDER BY booked_count DESC,day LIMIT 1) busy
    UNION ALL SELECT 'slowest',day::text,0,booked_count,percentage,NULL FROM (SELECT day,COUNT(*) FILTER(WHERE booked)::int AS booked_count,ROUND(100.0*COUNT(*) FILTER(WHERE booked)/NULLIF(COUNT(*),0),2) AS percentage FROM occupied GROUP BY day ORDER BY booked_count,day LIMIT 1) slow
    UNION ALL SELECT 'heatmap',EXTRACT(DOW FROM day)::text||':'||EXTRACT(HOUR FROM slot_start AT TIME ZONE @zone)::text,0,COUNT(*) FILTER(WHERE booked)::int,ROUND(100.0*COUNT(*) FILTER(WHERE booked)/NULLIF(COUNT(*),0),2),NULL FROM occupied GROUP BY EXTRACT(DOW FROM day),EXTRACT(HOUR FROM slot_start AT TIME ZONE @zone)
    """,
            _ => throw new ArgumentException("Unknown report")
        };
        var parameters = new object[] { new Npgsql.NpgsqlParameter("org", org), new Npgsql.NpgsqlParameter("venue", venue), new Npgsql.NpgsqlParameter("start", start), new Npgsql.NpgsqlParameter("end", end), new Npgsql.NpgsqlParameter("zone", zone), new Npgsql.NpgsqlParameter("previous", previousStart), new Npgsql.NpgsqlParameter("previousEnd", previousEnd), new Npgsql.NpgsqlParameter("cashier", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)cashier ?? DBNull.Value } };
        return await db.Database.SqlQueryRaw<ReportData>(sql, parameters).ToListAsync(ct);
    }
}
