using Futsal.Domain.Locations;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Locations;

public static class LocationCatalog
{
    private sealed record Seed(string Parent, LocationType Type, string En, string Ne, string Slug);
    private static readonly Seed[] Rows = [
        new("",LocationType.PROVINCE,"Koshi","कोशी","koshi"), new("",LocationType.PROVINCE,"Madhesh","मधेश","madhesh"), new("",LocationType.PROVINCE,"Bagmati","बागमती","bagmati"), new("",LocationType.PROVINCE,"Gandaki","गण्डकी","gandaki"), new("",LocationType.PROVINCE,"Lumbini","लुम्बिनी","lumbini"), new("",LocationType.PROVINCE,"Karnali","कर्णाली","karnali"), new("",LocationType.PROVINCE,"Sudurpashchim","सुदूरपश्चिम","sudurpashchim"),
        new("bagmati",LocationType.DISTRICT,"Kathmandu District","काठमाडौं जिल्ला","kathmandu-district"),new("bagmati",LocationType.DISTRICT,"Lalitpur District","ललितपुर जिल्ला","lalitpur-district"),new("bagmati",LocationType.DISTRICT,"Bhaktapur District","भक्तपुर जिल्ला","bhaktapur-district"),new("gandaki",LocationType.DISTRICT,"Kaski District","कास्की जिल्ला","kaski-district"),new("koshi",LocationType.DISTRICT,"Morang District","मोरङ जिल्ला","morang-district"),new("madhesh",LocationType.DISTRICT,"Parsa District","पर्सा जिल्ला","parsa-district"),new("bagmati",LocationType.DISTRICT,"Chitwan District","चितवन जिल्ला","chitwan-district"),new("lumbini",LocationType.DISTRICT,"Rupandehi District","रुपन्देही जिल्ला","rupandehi-district"),new("koshi",LocationType.DISTRICT,"Sunsari District","सुनसरी जिल्ला","sunsari-district"),new("bagmati",LocationType.DISTRICT,"Makwanpur District","मकवानपुर जिल्ला","makwanpur-district"),new("madhesh",LocationType.DISTRICT,"Dhanusha District","धनुषा जिल्ला","dhanusha-district"),new("lumbini",LocationType.DISTRICT,"Banke District","बाँके जिल्ला","banke-district"),new("sudurpashchim",LocationType.DISTRICT,"Kailali District","कैलाली जिल्ला","kailali-district"),new("karnali",LocationType.DISTRICT,"Surkhet District","सुर्खेत जिल्ला","surkhet-district"),new("bagmati",LocationType.DISTRICT,"Kavrepalanchok District","काभ्रेपलाञ्चोक जिल्ला","kavre-district"),new("koshi",LocationType.DISTRICT,"Jhapa District","झापा जिल्ला","jhapa-district"),new("lumbini",LocationType.DISTRICT,"Dang District","दाङ जिल्ला","dang-district"),
        new("kathmandu-district",LocationType.CITY,"Kathmandu","काठमाडौं","kathmandu"),new("lalitpur-district",LocationType.CITY,"Lalitpur","ललितपुर","lalitpur"),new("bhaktapur-district",LocationType.CITY,"Bhaktapur","भक्तपुर","bhaktapur"),new("kaski-district",LocationType.CITY,"Pokhara","पोखरा","pokhara"),new("morang-district",LocationType.CITY,"Biratnagar","विराटनगर","biratnagar"),new("parsa-district",LocationType.CITY,"Birgunj","वीरगञ्ज","birgunj"),new("chitwan-district",LocationType.CITY,"Bharatpur (Chitwan)","भरतपुर (चितवन)","bharatpur"),new("rupandehi-district",LocationType.CITY,"Butwal","बुटवल","butwal"),new("rupandehi-district",LocationType.CITY,"Bhairahawa","भैरहवा","bhairahawa"),new("sunsari-district",LocationType.CITY,"Dharan","धरान","dharan"),new("sunsari-district",LocationType.CITY,"Itahari","इटहरी","itahari"),new("makwanpur-district",LocationType.CITY,"Hetauda","हेटौंडा","hetauda"),new("dhanusha-district",LocationType.CITY,"Janakpur","जनकपुर","janakpur"),new("banke-district",LocationType.CITY,"Nepalgunj","नेपालगञ्ज","nepalgunj"),new("kailali-district",LocationType.CITY,"Dhangadhi","धनगढी","dhangadhi"),new("surkhet-district",LocationType.CITY,"Birendranagar","वीरेन्द्रनगर","birendranagar"),new("kavre-district",LocationType.CITY,"Dhulikhel","धुलिखेल","dhulikhel"),new("kavre-district",LocationType.CITY,"Banepa","बनेपा","banepa"),new("jhapa-district",LocationType.CITY,"Damak","दमक","damak"),new("dang-district",LocationType.CITY,"Tulsipur","तुलसीपुर","tulsipur"),
        new("kathmandu",LocationType.AREA,"Baneshwor","बानेश्वर","baneshwor"),new("kathmandu",LocationType.AREA,"Koteshwor","कोटेश्वर","koteshwor"),new("kathmandu",LocationType.AREA,"Chabahil","चाबहिल","chabahil"),new("kathmandu",LocationType.AREA,"Boudha","बौद्ध","boudha"),new("kathmandu",LocationType.AREA,"Baluwatar","बालुवाटार","baluwatar"),new("kathmandu",LocationType.AREA,"Maharajgunj","महाराजगञ्ज","maharajgunj"),new("kathmandu",LocationType.AREA,"Balaju","बालाजु","balaju"),new("kathmandu",LocationType.AREA,"Kalanki","कलंकी","kalanki"),new("kathmandu",LocationType.AREA,"Kalimati","कालिमाटी","kalimati"),new("kathmandu",LocationType.AREA,"Thamel","ठमेल","thamel"),new("kathmandu",LocationType.AREA,"Lazimpat","लाजिम्पाट","lazimpat"),new("lalitpur",LocationType.AREA,"Jawalakhel","जावलाखेल","jawalakhel"),new("lalitpur",LocationType.AREA,"Kupondole","कुपण्डोल","kupondole"),new("lalitpur",LocationType.AREA,"Satdobato","सातदोबाटो","satdobato"),new("lalitpur",LocationType.AREA,"Imadol","इमाडोल","imadol"),new("lalitpur",LocationType.AREA,"Gwarko","ग्वार्को","gwarko"),new("lalitpur",LocationType.AREA,"Bhaisepati","भैंसेपाटी","bhaisepati"),
        new("pokhara",LocationType.AREA,"Lakeside","लेकसाइड","lakeside"),new("pokhara",LocationType.AREA,"Chipledhunga","चिप्लेढुङ्गा","chipledhunga"),new("pokhara",LocationType.AREA,"Prithvi Chowk","पृथ्वी चोक","prithvi-chowk"),new("pokhara",LocationType.AREA,"Bagar","बगर","bagar"),new("pokhara",LocationType.AREA,"Mahendrapul","महेन्द्रपुल","mahendrapul")
    ];

    public static async Task SeedAsync(FutsalDbContext db, CancellationToken ct)
    {
        var rows = await db.Locations.IgnoreQueryFilters().ToListAsync(ct);
        foreach (var item in Rows)
        {
            var parentPath = item.Parent;
            var parent = item.Parent.Length == 0 ? null : rows.Single(x => x.Slug == item.Parent);
            if (rows.Any(x => x.Slug == item.Slug)) continue;
            var row = new Location { Type=item.Type, NameEn=item.En, NameNe=item.Ne, Slug=item.Slug, ParentId=parent?.Id, Path=parent is null ? item.Slug : parent.Path+"/"+item.Slug, IsActive=true, IsSeeded=true, SortOrder=Array.IndexOf(Rows,item) };
            db.Locations.Add(row); rows.Add(row);
        }
        var amenities = new[] { ("floodlights","Floodlights"),("parking","Parking"),("changing-room","Changing room"),("shower","Shower"),("canteen","Canteen"),("drinking-water","Drinking water"),("wifi","WiFi"),("jersey-rental","Jersey rental"),("ball-rental","Ball rental"),("indoor","Indoor"),("outdoor","Outdoor") };
        foreach (var item in amenities)
            if (!await db.Amenities.AnyAsync(x => x.Code == item.Item1, ct)) db.Amenities.Add(new Amenity { Code=item.Item1, NameEn=item.Item2 });
        await db.SaveChangesAsync(ct);
    }
}
