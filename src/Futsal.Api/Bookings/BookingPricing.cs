using Futsal.Domain.Bookings;
using Futsal.Domain.Locations;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Bookings;

public sealed record PriceBreakdown(decimal Gross,decimal Discount,decimal Total);

public sealed class BookingPricing(FutsalDbContext db)
{
    public async Task<PriceBreakdown> CalculateAsync(Court court,DateTimeOffset startAt,DateTimeOffset endAt,Venue venue,Customer? customer,CancellationToken ct)
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById(venue.Timezone);
        decimal gross=0;
        for(var slot=startAt;slot<endAt;slot=slot.AddMinutes(court.SlotMinutes))
        {
            var local=TimeZoneInfo.ConvertTime(slot,zone);var localEnd=local.AddMinutes(court.SlotMinutes);
            var weekday=(short)local.DayOfWeek;var date=DateOnly.FromDateTime(local.DateTime);var from=TimeOnly.FromDateTime(local.DateTime);var to=TimeOnly.FromDateTime(localEnd.DateTime);
            var rule=await db.PricingRules.IgnoreQueryFilters().Where(x=>x.CourtId==court.Id&&x.Weekdays.Contains(weekday)
                    &&(x.ValidFrom==null||x.ValidFrom<=date)&&(x.ValidTo==null||x.ValidTo>=date)&&x.FromTime<=from&&x.ToTime>=to)
                .OrderByDescending(x=>x.Priority).ThenBy(x=>x.Id).FirstOrDefaultAsync(ct);
            gross+=rule?.PricePerSlot??court.PricePerSlot;
        }
        var discount=customer?.IsRegular==true?decimal.Round(gross*customer.DiscountPercent/100m,2,MidpointRounding.AwayFromZero):0m;
        return new PriceBreakdown(gross,discount,gross-discount);
    }
}
