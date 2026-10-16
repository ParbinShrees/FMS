using Futsal.Domain.Notifications;
using Futsal.Domain.Pilot;
using Futsal.Domain.Scheduling;
using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Futsal.Domain.Locations;
using Futsal.Domain.Bookings;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Npgsql.NameTranslation;

namespace Futsal.Infrastructure.Persistence;

public sealed class FutsalDbContext(DbContextOptions<FutsalDbContext> options, ICurrentUser currentUser, IClock clock) : DbContext(options)
{
    private static readonly NpgsqlNullNameTranslator PostgresNames = new();
    public static void ConfigurePostgres(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder options)
    {
        var translator = PostgresNames;
        options.MapEnum<OrganizationRole>("member_role", nameTranslator: translator);
        options.MapEnum<BookingStatus>("booking_status", nameTranslator: translator);
        options.MapEnum<BookingSource>("booking_source", nameTranslator: translator);
        options.MapEnum<PaymentMethod>("payment_method", nameTranslator: translator);
        options.MapEnum<PaymentStatus>("payment_status", nameTranslator: translator);
    }
    public DbSet<RecurringBookingSeries> RecurringSeries=>Set<RecurringBookingSeries>();
    public DbSet<RecurringSeriesException> SeriesExceptions=>Set<RecurringSeriesException>();
    public DbSet<RecurringSeriesRevision> SeriesRevisions=>Set<RecurringSeriesRevision>();
    public DbSet<SeriesConflict> SeriesConflicts=>Set<SeriesConflict>();
    public DbSet<SeriesPrepayment> SeriesPrepayments=>Set<SeriesPrepayment>();
    public DbSet<WaitlistEntry> WaitlistEntries=>Set<WaitlistEntry>();
    public DbSet<OnboardingState> OnboardingStates=>Set<OnboardingState>();
    public DbSet<StaffInvite> StaffInvites=>Set<StaffInvite>();
    public DbSet<PilotFeedback> PilotFeedback=>Set<PilotFeedback>();
    public DbSet<PilotError> PilotErrors=>Set<PilotError>();
    public DbSet<BookingWatch> BookingWatches=>Set<BookingWatch>();
    public DbSet<Subscription> Subscriptions=>Set<Subscription>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<VenueAmenity> VenueAmenities => Set<VenueAmenity>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<CourtHour> CourtHours => Set<CourtHour>();
    public DbSet<BlockedPeriod> BlockedPeriods => Set<BlockedPeriod>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();
    public DbSet<VenuePaymentSettings> VenuePaymentSettings => Set<VenuePaymentSettings>();

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationSettings> NotificationSettings => Set<NotificationSettings>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<SmsUsage> SmsUsage => Set<SmsUsage>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigurePilot(modelBuilder);
        ConfigureScheduling(modelBuilder);
        modelBuilder.Entity<Subscription>(e=>{e.ToTable("subscriptions");e.HasKey(x=>x.Id);e.HasIndex(x=>x.OrganizationId).IsUnique();e.HasQueryFilter(x=>currentUser.IsSuperAdmin||(currentUser.OrganizationId!=null&&x.OrganizationId==currentUser.OrganizationId));foreach(var prop in e.Metadata.GetProperties())prop.SetColumnName(new NpgsqlSnakeCaseNameTranslator().TranslateMemberName(prop.Name));});
        modelBuilder.Entity<RefreshToken>().Property(x=>x.FamilyId).HasColumnName("family_id");
        modelBuilder.Entity<AppUser>().Property(x=>x.TotpEnabled).HasColumnName("totp_enabled");
        modelBuilder.Entity<AppUser>().Property(x=>x.TotpSecret).HasColumnName("totp_secret");
        modelBuilder.Entity<AppUser>().Property(x=>x.TotpLastStep).HasColumnName("totp_last_step");
        modelBuilder.Entity<AppUser>().Property(x=>x.RecoveryCodeHashes).HasColumnName("recovery_code_hashes");
        modelBuilder.Entity<Booking>().HasIndex(x=>new{x.OrganizationId,x.VenueId,x.StartAt}).HasDatabaseName("ix_bookings_org_venue_start");
        modelBuilder.Entity<Payment>().HasIndex(x=>new{x.OrganizationId,x.BookingId,x.CreatedAt}).HasDatabaseName("ix_payments_org_booking_created");
        modelBuilder.Entity<Notification>().HasIndex(x=>new{x.OrganizationId,x.CustomerId,x.Template,x.CreatedAt}).HasDatabaseName("ix_notifications_due_throttle");
        modelBuilder.Entity<RefreshToken>().HasIndex(x=>x.FamilyId).HasDatabaseName("ix_refresh_family");
        modelBuilder.Entity<Venue>().Property(x=>x.BsFirst).HasColumnName("bs_first");
        modelBuilder.Entity<Venue>().Property(x=>x.NepaliNumerals).HasColumnName("nepali_numerals");
        modelBuilder.Entity<Payment>().Property(x=>x.ReversalOf).HasColumnName("reversal_of");
        modelBuilder.Entity<Payment>().HasIndex(x=>x.ReversalOf).HasDatabaseName("ix_payment_reversals");
        ConfigureNotifications(modelBuilder);
        modelBuilder.Entity<Customer>().Property(x => x.OptOut).HasColumnName("opt_out");
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.HasPostgresExtension("pgcrypto");
        modelBuilder.HasPostgresEnum<OrganizationRole>(name: "member_role", nameTranslator: new NpgsqlNullNameTranslator());
        modelBuilder.HasPostgresEnum<BookingStatus>(name: "booking_status", nameTranslator: new NpgsqlNullNameTranslator());
        modelBuilder.HasPostgresEnum<BookingSource>(name: "booking_source", nameTranslator: new NpgsqlNullNameTranslator());
        modelBuilder.HasPostgresEnum<PaymentMethod>(name: "payment_method", nameTranslator: new NpgsqlNullNameTranslator());
        modelBuilder.HasPostgresEnum<PaymentStatus>(name: "payment_status", nameTranslator: new NpgsqlNullNameTranslator());

        modelBuilder.Entity<Location>(e =>
        {
            e.ToTable("locations"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.ParentId).HasColumnName("parent_id");
            e.Property(x => x.Type).HasColumnName("type").HasConversion<string>();
            e.Property(x => x.NameEn).HasColumnName("name_en").IsRequired();
            e.Property(x => x.NameNe).HasColumnName("name_ne").IsRequired();
            e.Property(x => x.Slug).HasColumnName("slug").IsRequired();
            e.Property(x => x.Latitude).HasColumnName("latitude"); e.Property(x => x.Longitude).HasColumnName("longitude");
            e.Property(x => x.Path).HasColumnName("path").IsRequired(); e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.IsSeeded).HasColumnName("is_seeded"); e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.HasIndex(x => x.Slug).IsUnique(); e.HasIndex(x => new { x.ParentId, x.Slug }).IsUnique();
            e.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Venue>(e =>
        {
            e.ToTable("venues"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.OrganizationId).HasColumnName("organization_id"); e.Property(x => x.LocationId).HasColumnName("location_id");
            e.Property(x => x.CityId).HasColumnName("city_id"); e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Slug).HasColumnName("slug").IsRequired(); e.Property(x => x.Address).HasColumnName("address");
            e.Property(x => x.Latitude).HasColumnName("latitude"); e.Property(x => x.Longitude).HasColumnName("longitude");
            e.Property(x => x.PhotoPlaceholder).HasColumnName("photo_placeholder"); e.Property(x => x.IsPublic).HasColumnName("is_public");
            e.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            e.Property(x => x.Timezone).HasColumnName("timezone").HasDefaultValue("Asia/Kathmandu");
            e.Property(x => x.HoldMinutes).HasColumnName("hold_minutes").HasDefaultValue(10);
            e.Property(x => x.FreeCancelHours).HasColumnName("free_cancel_hours").HasDefaultValue(6);
            e.Property(x => x.AdvanceType).HasColumnName("advance_type").HasDefaultValue("PERCENT");
            e.Property(x => x.AdvanceValue).HasColumnName("advance_value").HasPrecision(12,2).HasDefaultValue(30m);
            e.HasIndex(x => x.Slug).IsUnique(); e.HasIndex(x => new { x.CityId, x.LocationId });
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.CityId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => currentUser.IsSuperAdmin || (currentUser.OrganizationId != null && x.OrganizationId == currentUser.OrganizationId));
        });
        modelBuilder.Entity<Court>(e =>
        {
            e.ToTable("courts"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.OrganizationId).HasColumnName("organization_id"); e.Property(x => x.VenueId).HasColumnName("venue_id");
            e.Property(x => x.Name).HasColumnName("name").IsRequired(); e.Property(x => x.Surface).HasColumnName("surface");
            e.Property(x => x.PricePerSlot).HasColumnName("price_per_slot").HasPrecision(12, 2); e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.SlotMinutes).HasColumnName("slot_minutes").HasDefaultValue(60);
            e.HasOne(x => x.Venue).WithMany(x => x.Courts).HasForeignKey(x => x.VenueId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentUser.IsSuperAdmin || (currentUser.OrganizationId != null && x.OrganizationId == currentUser.OrganizationId));
        });
        modelBuilder.Entity<Amenity>(e =>
        {
            e.ToTable("amenities"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Code).HasColumnName("code").IsRequired(); e.Property(x => x.NameEn).HasColumnName("name_en").IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active"); e.HasIndex(x => x.Code).IsUnique();
        });
        modelBuilder.Entity<VenueAmenity>(e =>
        {
            e.ToTable("venue_amenities"); e.HasKey(x => new { x.VenueId, x.AmenityId });
            e.Property(x => x.VenueId).HasColumnName("venue_id"); e.Property(x => x.AmenityId).HasColumnName("amenity_id");
            e.HasOne(x => x.Venue).WithMany(x => x.VenueAmenities).HasForeignKey(x => x.VenueId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Amenity).WithMany().HasForeignKey(x => x.AmenityId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Customer>(e =>
        {
            e.ToTable("customers", t => t.HasCheckConstraint("ck_customers_discount", "discount_percent >= 0 AND discount_percent <= 100"));
            e.HasKey(x=>x.Id); e.Property(x=>x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x=>x.OrganizationId).HasColumnName("organization_id"); e.Property(x=>x.UserId).HasColumnName("user_id");
            e.Property(x=>x.FullName).HasColumnName("full_name").IsRequired(); e.Property(x=>x.Phone).HasColumnName("phone").IsRequired();
            e.Property(x=>x.Notes).HasColumnName("notes");e.Property(x=>x.IsRegular).HasColumnName("is_regular");
            e.Property(x=>x.DiscountPercent).HasColumnName("discount_percent").HasPrecision(5,2);e.Property(x=>x.NoShowCount).HasColumnName("no_show_count");
            e.Property(x=>x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasIndex(x=>new{x.OrganizationId,x.Phone}).IsUnique();e.HasOne<Organization>().WithMany().HasForeignKey(x=>x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x=>currentUser.IsSuperAdmin||(currentUser.OrganizationId!=null&&x.OrganizationId==currentUser.OrganizationId));
        });
        modelBuilder.Entity<Booking>(e =>
        {
            e.ToTable("bookings",t=>{t.HasCheckConstraint("ck_bookings_time","end_at > start_at");t.HasCheckConstraint("ck_bookings_paid","paid_amount >= 0");});
            e.HasKey(x=>x.Id);e.Property(x=>x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x=>x.OrganizationId).HasColumnName("organization_id");e.Property(x=>x.VenueId).HasColumnName("venue_id");e.Property(x=>x.CourtId).HasColumnName("court_id");
            e.Property(x=>x.CustomerId).HasColumnName("customer_id");e.Property(x=>x.BookingCode).HasColumnName("booking_code").IsRequired();
            e.Property(x=>x.StartAt).HasColumnName("start_at");e.Property(x=>x.EndAt).HasColumnName("end_at");
            e.Property(x=>x.Status).HasColumnName("status").HasColumnType("booking_status");e.Property(x=>x.Source).HasColumnName("source").HasColumnType("booking_source");
            e.Property(x=>x.HoldExpiresAt).HasColumnName("hold_expires_at");e.Property(x=>x.TotalAmount).HasColumnName("total_amount").HasPrecision(12,2);
            e.Property(x=>x.DiscountAmount).HasColumnName("discount_amount").HasPrecision(12,2).HasDefaultValue(0m);
            e.Property(x=>x.AdvanceRequired).HasColumnName("advance_required").HasPrecision(12,2).HasDefaultValue(0m);
            e.Property(x=>x.PaidAmount).HasColumnName("paid_amount").HasPrecision(12,2).HasDefaultValue(0m);
            e.Property(x=>x.Notes).HasColumnName("notes");e.Property(x=>x.CancelReason).HasColumnName("cancel_reason");e.Property(x=>x.CancelledAt).HasColumnName("cancelled_at");
            e.Property(x=>x.CreatedBy).HasColumnName("created_by");e.Property(x=>x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");e.Property(x=>x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
            e.HasIndex(x=>x.BookingCode).IsUnique();e.HasIndex(x=>new{x.VenueId,x.StartAt}).HasDatabaseName("ix_bookings_venue_start");
            e.HasIndex(x=>x.HoldExpiresAt).HasDatabaseName("ix_bookings_hold").HasFilter("status = 'HELD'");e.HasIndex(x=>x.CustomerId).HasDatabaseName("ix_bookings_customer");
            e.HasOne<Organization>().WithMany().HasForeignKey(x=>x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Venue>().WithMany().HasForeignKey(x=>x.VenueId).OnDelete(DeleteBehavior.Restrict);e.HasOne<Court>().WithMany().HasForeignKey(x=>x.CourtId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Customer>().WithMany().HasForeignKey(x=>x.CustomerId).OnDelete(DeleteBehavior.SetNull);e.HasOne<AppUser>().WithMany().HasForeignKey(x=>x.CreatedBy).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x=>currentUser.IsSuperAdmin||(currentUser.OrganizationId!=null&&x.OrganizationId==currentUser.OrganizationId));
        });
        modelBuilder.Entity<Payment>(e =>
        {
            e.ToTable("payments",t=>t.HasCheckConstraint("ck_payments_nonzero","amount <> 0"));e.HasKey(x=>x.Id);e.Property(x=>x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x=>x.OrganizationId).HasColumnName("organization_id");e.Property(x=>x.BookingId).HasColumnName("booking_id");
            e.Property(x=>x.Method).HasColumnName("method").HasColumnType("payment_method");e.Property(x=>x.Status).HasColumnName("status").HasColumnType("payment_status");
            e.Property(x=>x.Amount).HasColumnName("amount").HasPrecision(12,2);e.Property(x=>x.GatewayTxnUuid).HasColumnName("gateway_txn_uuid");e.Property(x=>x.GatewayRef).HasColumnName("gateway_ref");
            e.Property(x=>x.RawResponse).HasColumnName("raw_response").HasColumnType("jsonb");e.Property(x=>x.ReceivedBy).HasColumnName("received_by");
            e.Property(x=>x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");e.Property(x=>x.VerifiedAt).HasColumnName("verified_at");
            e.HasIndex(x=>x.GatewayTxnUuid).IsUnique();e.HasOne<Organization>().WithMany().HasForeignKey(x=>x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Booking>().WithMany().HasForeignKey(x=>x.BookingId).OnDelete(DeleteBehavior.SetNull);e.HasOne<AppUser>().WithMany().HasForeignKey(x=>x.ReceivedBy).OnDelete(DeleteBehavior.SetNull);
            e.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();e.HasQueryFilter(x=>currentUser.IsSuperAdmin||(currentUser.OrganizationId!=null&&x.OrganizationId==currentUser.OrganizationId));
        });
        modelBuilder.Entity<IdempotencyKey>(e=>
        {
            e.ToTable("idempotency_keys");e.HasKey(x=>new{x.Key,x.UserId,x.Endpoint});e.Property(x=>x.Key).HasColumnName("key");e.Property(x=>x.UserId).HasColumnName("user_id");
            e.Property(x=>x.Endpoint).HasColumnName("endpoint");e.Property(x=>x.ResponseStatus).HasColumnName("response_status");e.Property(x=>x.ResponseBody).HasColumnName("response_body").HasColumnType("jsonb");
            e.Property(x=>x.RequestHash).HasColumnName("request_hash");e.Property(x=>x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<CourtHour>(e=>
        {
            e.ToTable("court_hours",t=>t.HasCheckConstraint("ck_court_hours_weekday","weekday between 0 and 6"));e.HasKey(x=>x.Id);e.Property(x=>x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x=>x.CourtId).HasColumnName("court_id");e.Property(x=>x.Weekday).HasColumnName("weekday");e.Property(x=>x.OpenTime).HasColumnName("open_time");e.Property(x=>x.CloseTime).HasColumnName("close_time");
            e.HasIndex(x=>new{x.CourtId,x.Weekday}).IsUnique();e.HasOne<Court>().WithMany().HasForeignKey(x=>x.CourtId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<BlockedPeriod>(e=>
        {
            e.ToTable("blocked_periods",t=>t.HasCheckConstraint("ck_blocked_periods_time","end_at > start_at"));e.HasKey(x=>x.Id);e.Property(x=>x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x=>x.OrganizationId).HasColumnName("organization_id");e.Property(x=>x.CourtId).HasColumnName("court_id");e.Property(x=>x.StartAt).HasColumnName("start_at");e.Property(x=>x.EndAt).HasColumnName("end_at");e.Property(x=>x.Reason).HasColumnName("reason");
            e.HasOne<Court>().WithMany().HasForeignKey(x=>x.CourtId).OnDelete(DeleteBehavior.Cascade);e.HasQueryFilter(x=>currentUser.IsSuperAdmin||(currentUser.OrganizationId!=null&&x.OrganizationId==currentUser.OrganizationId));
        });
        modelBuilder.Entity<PricingRule>(e=>
        {
            e.ToTable("pricing_rules");e.HasKey(x=>x.Id);e.Property(x=>x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");e.Property(x=>x.OrganizationId).HasColumnName("organization_id");e.Property(x=>x.CourtId).HasColumnName("court_id");
            e.Property(x=>x.Name).HasColumnName("name").IsRequired();e.Property(x=>x.Weekdays).HasColumnName("weekdays");e.Property(x=>x.FromTime).HasColumnName("from_time");e.Property(x=>x.ToTime).HasColumnName("to_time");
            e.Property(x=>x.PricePerSlot).HasColumnName("price_per_slot").HasPrecision(12,2);e.Property(x=>x.Priority).HasColumnName("priority");e.Property(x=>x.ValidFrom).HasColumnName("valid_from");e.Property(x=>x.ValidTo).HasColumnName("valid_to");
            e.HasOne<Court>().WithMany().HasForeignKey(x=>x.CourtId).OnDelete(DeleteBehavior.Cascade);e.HasQueryFilter(x=>currentUser.IsSuperAdmin||(currentUser.OrganizationId!=null&&x.OrganizationId==currentUser.OrganizationId));
        });
        modelBuilder.Entity<VenuePaymentSettings>(e =>
        {
            e.ToTable("venue_payment_settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.VenueId).HasColumnName("venue_id");
            e.Property(x => x.Method).HasColumnName("method").HasColumnType("payment_method");
            e.Property(x => x.IsEnabled).HasColumnName("is_enabled");
            e.Property(x => x.MerchantCode).HasColumnName("merchant_code").IsRequired();
            e.Property(x => x.EncryptedSecret).HasColumnName("encrypted_secret").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.HasIndex(x => new { x.VenueId, x.Method }).IsUnique();
            e.HasOne(x => x.Venue).WithMany(x => x.PaymentSettings).HasForeignKey(x => x.VenueId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentUser.IsSuperAdmin || (currentUser.OrganizationId != null && x.OrganizationId == currentUser.OrganizationId));
        });

        modelBuilder.Entity<Venue>().HasQueryFilter(x=>(currentUser.IsSuperAdmin || (currentUser.OrganizationId!=null && x.OrganizationId==currentUser.OrganizationId && Memberships.Any(m=>m.UserId==currentUser.UserId && m.OrganizationId==x.OrganizationId && (m.VenueId==null || m.VenueId==x.Id)))));
        modelBuilder.Entity<Court>().HasQueryFilter(x=>(currentUser.IsSuperAdmin || (currentUser.OrganizationId!=null && x.OrganizationId==currentUser.OrganizationId && Memberships.Any(m=>m.UserId==currentUser.UserId && m.OrganizationId==x.OrganizationId && (m.VenueId==null || m.VenueId==x.VenueId)))));
        modelBuilder.Entity<Booking>().HasQueryFilter(x=>(currentUser.IsSuperAdmin || (currentUser.OrganizationId!=null && x.OrganizationId==currentUser.OrganizationId && Memberships.Any(m=>m.UserId==currentUser.UserId && m.OrganizationId==x.OrganizationId && (m.VenueId==null || m.VenueId==x.VenueId)))));
        modelBuilder.Entity<Notification>().HasQueryFilter(x=>(currentUser.IsSuperAdmin || (currentUser.OrganizationId!=null && x.OrganizationId==currentUser.OrganizationId && Memberships.Any(m=>m.UserId==currentUser.UserId && m.OrganizationId==x.OrganizationId && (m.VenueId==null || m.VenueId==x.VenueId)))));
        modelBuilder.Entity<NotificationSettings>().HasQueryFilter(x=>(currentUser.IsSuperAdmin || (currentUser.OrganizationId!=null && x.OrganizationId==currentUser.OrganizationId && Memberships.Any(m=>m.UserId==currentUser.UserId && m.OrganizationId==x.OrganizationId && (m.VenueId==null || m.VenueId==x.VenueId)))));
        modelBuilder.Entity<NotificationTemplate>().HasQueryFilter(x=>(currentUser.IsSuperAdmin || (currentUser.OrganizationId!=null && x.OrganizationId==currentUser.OrganizationId && Memberships.Any(m=>m.UserId==currentUser.UserId && m.OrganizationId==x.OrganizationId && (m.VenueId==null || m.VenueId==x.VenueId)))));
        modelBuilder.Entity<SmsUsage>().HasQueryFilter(x=>(currentUser.IsSuperAdmin || (currentUser.OrganizationId!=null && x.OrganizationId==currentUser.OrganizationId && Memberships.Any(m=>m.UserId==currentUser.UserId && m.OrganizationId==x.OrganizationId && (m.VenueId==null || m.VenueId==x.VenueId)))));
        modelBuilder.Entity<VenuePaymentSettings>().HasQueryFilter(x=>(currentUser.IsSuperAdmin || (currentUser.OrganizationId!=null && x.OrganizationId==currentUser.OrganizationId && Memberships.Any(m=>m.UserId==currentUser.UserId && m.OrganizationId==x.OrganizationId && (m.VenueId==null || m.VenueId==x.VenueId)))));
        modelBuilder.Entity<Payment>().HasQueryFilter(x=>currentUser.IsSuperAdmin||(currentUser.OrganizationId!=null&&x.OrganizationId==currentUser.OrganizationId&&Bookings.Any(b=>b.Id==x.BookingId)));
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(x => x.Phone).HasColumnName("phone");
            entity.HasIndex(x => x.Phone).IsUnique();
            entity.Property(x => x.Email).HasColumnName("email").HasColumnType("citext");
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.FullName).HasColumnName("full_name").IsRequired();
            entity.Property(x => x.PasswordHash).HasColumnName("password_hash");
            entity.Property(x => x.IsPlatformAdmin).HasColumnName("is_platform_admin");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            entity.Property(x => x.FailedLoginCount).HasColumnName("failed_login_count");
            entity.Property(x => x.LockedUntil).HasColumnName("locked_until");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("organizations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(x => x.Name).HasColumnName("name").IsRequired();
            entity.Property(x => x.PanNumber).HasColumnName("pan_number");
            entity.Property(x => x.Phone).HasColumnName("phone");
            entity.Property(x => x.Email).HasColumnName("email").HasColumnType("citext");
            entity.Property(x => x.Status).HasColumnName("status").HasDefaultValue("ACTIVE");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.HasQueryFilter(x => currentUser.IsSuperAdmin || (currentUser.OrganizationId != null && x.Id == currentUser.OrganizationId));
        });

        modelBuilder.Entity<Membership>(entity =>
        {
            entity.ToTable("memberships");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Role).HasColumnName("role").HasColumnType("member_role");
            entity.Property(x => x.VenueId).HasColumnName("venue_id");
            entity.HasIndex(x => new { x.OrganizationId, x.UserId, x.VenueId }).IsUnique();
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => currentUser.IsSuperAdmin || (currentUser.OrganizationId != null && x.OrganizationId == currentUser.OrganizationId));
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            entity.Property(x => x.ReplacedBy).HasColumnName("replaced_by");
            entity.Property(x => x.CreatedIp).HasColumnName("created_ip");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RefreshToken>().WithMany().HasForeignKey(x => x.ReplacedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OtpCode>(entity =>
        {
            entity.ToTable("otp_codes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(x => x.Phone).HasColumnName("phone").IsRequired();
            entity.Property(x => x.CodeHash).HasColumnName("code_hash").IsRequired();
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.Property(x => x.Attempts).HasColumnName("attempts");
            entity.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.HasIndex(x => new { x.Phone, x.CreatedAt });
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Action).HasColumnName("action").IsRequired();
            entity.Property(x => x.EntityType).HasColumnName("entity_type").IsRequired();
            entity.Property(x => x.EntityId).HasColumnName("entity_id");
            entity.Property(x => x.Before).HasColumnName("before").HasColumnType("jsonb");
            entity.Property(x => x.After).HasColumnName("after").HasColumnType("jsonb");
            entity.Property(x => x.Ip).HasColumnName("ip");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.HasQueryFilter(x => currentUser.IsSuperAdmin || (currentUser.OrganizationId != null && x.OrganizationId == currentUser.OrganizationId));
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RecordRoleChanges();
        EnforceTenantWriteScope();
        ValidateVenuesAsync(CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RecordRoleChanges();
        EnforceTenantWriteScope();
        var courtOrgs=ChangeTracker.Entries<Court>().Where(e=>e.State==EntityState.Added||e.State==EntityState.Modified).Select(e=>e.Entity.OrganizationId).Distinct().Order().ToArray();
        await using var capTx=Database.IsRelational()&&courtOrgs.Length>0&&Database.CurrentTransaction==null?await Database.BeginTransactionAsync(cancellationToken):null;
        if(Database.IsRelational())foreach(var org in courtOrgs)await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"court-cap:"+org},0))",cancellationToken);
        await ValidateVenuesAsync(cancellationToken);
        var saved=await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if(capTx!=null)await capTx.CommitAsync(cancellationToken);
        return saved;
    }

    private async Task ValidateVenuesAsync(CancellationToken cancellationToken)
    {
        foreach(var group in ChangeTracker.Entries<Court>().Where(x=>x.State==EntityState.Added||(x.State==EntityState.Modified&&x.Entity.IsActive&&x.Property(c=>c.IsActive).IsModified)).GroupBy(x=>x.Entity.OrganizationId))
        {
            var license=await Subscriptions.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.OrganizationId==group.Key,cancellationToken);
            if(license!=null&&await Courts.IgnoreQueryFilters().CountAsync(x=>x.OrganizationId==group.Key&&x.IsActive,cancellationToken)+group.Count(x=>x.Entity.IsActive)>license.MaxCourts)throw new Futsal.Domain.Bookings.BookingDomainException("COURT_LIMIT_REACHED","Court limit reached.");
        }

        foreach (var entry in ChangeTracker.Entries<Location>().Where(x => x.State == EntityState.Added))
        {
            if (currentUser.IsSuperAdmin || entry.Entity.IsSeeded) continue;
            if (entry.Entity.ParentId is not Guid parentId
                || !await Locations.AnyAsync(x => x.Id == parentId && x.Type == LocationType.CITY && x.IsActive, cancellationToken)
                || currentUser.OrganizationId is not Guid orgId
                || !await Venues.AnyAsync(x => x.OrganizationId == orgId && x.CityId == parentId, cancellationToken))
                throw new UnauthorizedAccessException("Owners may add Areas only under a City used by their organization.");
        }
        foreach (var entry in ChangeTracker.Entries<Venue>().Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            var venue = entry.Entity;
            if ((venue.Latitude is null) != (venue.Longitude is null) || venue.Latitude is < 26 or > 31 || venue.Longitude is < 80 or > 89)
                throw new InvalidOperationException("Venue coordinates must be within Nepal (latitude 26–31, longitude 80–89).");
            var location = await Locations.IgnoreQueryFilters().Include(x => x.Parent).SingleOrDefaultAsync(x => x.Id == venue.LocationId, cancellationToken);
            if (location is null || location.Type is not (LocationType.CITY or LocationType.AREA))
                throw new InvalidOperationException("A venue must belong to a City or Area.");
            var cityId = location.Type == LocationType.CITY ? location.Id : location.ParentId;
            if (cityId is null || venue.CityId != cityId)
                throw new InvalidOperationException("Venue city_id must match its City location or the parent City of its Area.");
        }
    }

    private void EnforceTenantWriteScope()
    {
        foreach (var entry in ChangeTracker.Entries<AuditLog>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Audit entries are immutable.");
        }

        foreach (var entry in ChangeTracker.Entries<Payment>().Where(x => x.State is EntityState.Deleted))
            throw new InvalidOperationException("Payment entries cannot be deleted.");

        foreach (var entry in ChangeTracker.Entries<Payment>().Where(x => x.State is EntityState.Modified))
        {
            var originalStatus = entry.Property(x => x.Status).OriginalValue;
            if (originalStatus != PaymentStatus.PENDING)
                throw new InvalidOperationException("Payment entries are immutable; record corrections as a new payment.");
        }

        if(ChangeTracker.Entries<SeriesPrepayment>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted))throw new InvalidOperationException("Prepayment receipts are immutable.");
        if (currentUser.IsSuperAdmin) return;

        foreach (var entry in ChangeTracker.Entries<Location>().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Entity.IsSeeded && entry.State == EntityState.Added) continue;
            if (!currentUser.IsAuthenticated || entry.State != EntityState.Added || entry.Entity.Type != LocationType.AREA || currentUser.Role != "Owner")
                throw new UnauthorizedAccessException("Only a SuperAdmin can edit global locations; Owners may add Areas.");
        }

        if (!currentUser.IsAuthenticated) return;

        foreach (var entry in ChangeTracker.Entries<IOrganizationScoped>().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (currentUser.OrganizationId is not null && entry.Entity.OrganizationId == currentUser.OrganizationId) continue;
            if (currentUser.Role == "Customer" && currentUser.UserId is Guid customerUserId && currentUser.OrganizationId is null)
            {
                var ownedCustomers=ChangeTracker.Entries<Customer>().Where(x=>x.Entity.UserId==customerUserId).Select(x=>x.Entity.Id).ToHashSet();
                if(entry.Entity is Customer customer && customer.UserId==customerUserId)continue;
                if(entry.Entity is Booking booking && booking.CustomerId is Guid bookingCustomerId && ownedCustomers.Contains(bookingCustomerId))continue;
                if(entry.Entity is WaitlistEntry wait&&ownedCustomers.Contains(wait.CustomerId))continue;
                if(entry.Entity is Payment)continue;
            }
            throw new UnauthorizedAccessException("The current user cannot change data outside their organization.");
        }

        foreach (var entry in ChangeTracker.Entries<Organization>().Where(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Entity.Id != currentUser.OrganizationId)
                throw new UnauthorizedAccessException("The current user cannot change another organization.");
        }
    }

    private void RecordRoleChanges()
    {
        foreach (var entry in ChangeTracker.Entries<Membership>().Where(x => x.State == EntityState.Modified && x.Property(y => y.Role).IsModified))
        {
            var previousRole = entry.Property(x => x.Role).OriginalValue;
            var currentRole = entry.Entity.Role;
            if (previousRole == currentRole) continue;

            AuditLogs.Add(new AuditLog
            {
                OrganizationId = entry.Entity.OrganizationId,
                UserId = currentUser.UserId,
                Action = "ROLE_CHANGED",
                EntityType = "Membership",
                EntityId = entry.Entity.Id,
                Before = JsonSerializer.Serialize(new { role = RoleName(previousRole) }),
                After = JsonSerializer.Serialize(new { role = RoleName(currentRole) }),
                CreatedAt = clock.UtcNow
            });
        }
    }

    private void ConfigureNotifications(ModelBuilder model)
    {
        model.Entity<NotificationJobRun>(e => { e.ToTable("notification_job_runs"); e.HasKey(x => x.Name); e.Property(x => x.Name).HasColumnName("name"); e.Property(x => x.NextRun).HasColumnName("next_run"); });
        model.Entity<Notification>(e => { e.ToTable("notifications"); e.HasKey(x => x.Id); e.HasIndex(x => x.UniqueKey).IsUnique(); e.HasIndex(x => new { x.Status, x.ScheduledFor }); e.HasQueryFilter(x => currentUser.IsSuperAdmin || (currentUser.OrganizationId != null && x.OrganizationId == currentUser.OrganizationId)); });
        model.Entity<NotificationSettings>(e => { e.ToTable("notification_settings"); e.HasKey(x => x.Id); e.HasIndex(x => x.VenueId).IsUnique(); e.HasQueryFilter(x => currentUser.IsSuperAdmin || (currentUser.OrganizationId != null && x.OrganizationId == currentUser.OrganizationId)); });
        model.Entity<NotificationTemplate>(e => { e.ToTable("notification_templates"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.VenueId, x.Name, x.Language }).IsUnique(); e.HasQueryFilter(x => currentUser.IsSuperAdmin || (currentUser.OrganizationId != null && x.OrganizationId == currentUser.OrganizationId)); });
        model.Entity<SmsUsage>(e => { e.ToTable("sms_usage"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.VenueId, x.Day }).IsUnique(); e.HasQueryFilter(x => currentUser.IsSuperAdmin || (currentUser.OrganizationId != null && x.OrganizationId == currentUser.OrganizationId)); });
        foreach (var type in new[] { typeof(Notification), typeof(NotificationSettings), typeof(NotificationTemplate), typeof(SmsUsage) })
            foreach (var property in model.Entity(type).Metadata.GetProperties()) property.SetColumnName(new NpgsqlSnakeCaseNameTranslator().TranslateMemberName(property.Name));
    }

    private void ConfigureScheduling(ModelBuilder m)
    {
        SchedulingTable<RecurringBookingSeries>(m,"recurring_booking_series");SchedulingTable<RecurringSeriesException>(m,"recurring_series_exceptions");SchedulingTable<RecurringSeriesRevision>(m,"recurring_series_revisions");SchedulingTable<SeriesConflict>(m,"series_conflicts");SchedulingTable<SeriesPrepayment>(m,"series_prepayments");SchedulingTable<WaitlistEntry>(m,"waitlist_entries");
        m.Entity<RecurringBookingSeries>().HasOne<Court>().WithMany().HasForeignKey(x=>x.CourtId).OnDelete(DeleteBehavior.Restrict);m.Entity<RecurringBookingSeries>().HasOne<Customer>().WithMany().HasForeignKey(x=>x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<RecurringBookingSeries>().Property(x=>x.LockedPricePerOccurrence).HasPrecision(12,2);
        m.Entity<RecurringBookingSeries>().Property<uint>("xmin").IsRowVersion();m.Entity<WaitlistEntry>().Property<uint>("xmin").IsRowVersion();
        m.Entity<RecurringSeriesException>().HasOne<RecurringBookingSeries>().WithMany().HasForeignKey(x=>x.SeriesId).OnDelete(DeleteBehavior.Cascade);m.Entity<RecurringSeriesException>().HasIndex(x=>new{x.SeriesId,x.Date}).IsUnique();
        m.Entity<RecurringSeriesRevision>().HasOne<RecurringBookingSeries>().WithMany().HasForeignKey(x=>x.SeriesId).OnDelete(DeleteBehavior.Cascade);m.Entity<RecurringSeriesRevision>().HasIndex(x=>new{x.SeriesId,x.FromDate}).IsUnique();
        m.Entity<SeriesConflict>().HasOne<RecurringBookingSeries>().WithMany().HasForeignKey(x=>x.SeriesId).OnDelete(DeleteBehavior.Cascade);m.Entity<SeriesConflict>().HasIndex(x=>new{x.SeriesId,x.Date}).IsUnique();
        m.Entity<SeriesPrepayment>().HasOne<RecurringBookingSeries>().WithMany().HasForeignKey(x=>x.SeriesId).OnDelete(DeleteBehavior.Restrict);m.Entity<SeriesPrepayment>().Property(x=>x.Amount).HasPrecision(12,2);
        m.Entity<WaitlistEntry>().HasOne<Customer>().WithMany().HasForeignKey(x=>x.CustomerId).OnDelete(DeleteBehavior.Restrict);m.Entity<WaitlistEntry>().HasOne<Court>().WithMany().HasForeignKey(x=>x.CourtId).OnDelete(DeleteBehavior.Restrict);m.Entity<WaitlistEntry>().HasOne<Booking>().WithMany().HasForeignKey(x=>x.OfferedBookingId).OnDelete(DeleteBehavior.SetNull);
        m.Entity<WaitlistEntry>().Property(x=>x.Position).UseIdentityAlwaysColumn();m.Entity<WaitlistEntry>().HasIndex(x=>new{x.VenueId,x.Date,x.Status,x.Position});
        m.Entity<Booking>().Property(x=>x.SeriesId).HasColumnName("series_id");m.Entity<Booking>().Property(x=>x.SeriesDate).HasColumnName("series_date");m.Entity<Booking>().HasOne<RecurringBookingSeries>().WithMany().HasForeignKey(x=>x.SeriesId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Booking>().HasIndex(x=>new{x.SeriesId,x.SeriesDate}).IsUnique().HasFilter("series_id IS NOT NULL AND status NOT IN ('CANCELLED','EXPIRED')");
        m.Entity<Payment>().Property(x=>x.PrepaymentBlockId).HasColumnName("prepayment_block_id");m.Entity<Payment>().HasOne<SeriesPrepayment>().WithMany().HasForeignKey(x=>x.PrepaymentBlockId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Venue>().Property(x=>x.RecurringHorizonWeeks).HasColumnName("recurring_horizon_weeks").HasDefaultValue(8);m.Entity<Venue>().Property(x=>x.WaitlistOfferMinutes).HasColumnName("waitlist_offer_minutes").HasDefaultValue(15);m.Entity<Venue>().Property(x=>x.WaitlistNoShowThreshold).HasColumnName("waitlist_no_show_threshold").HasDefaultValue(3).HasSentinel(-1);m.Entity<Venue>().Property(x=>x.WeeklySeriesSummary).HasColumnName("weekly_series_summary");
    }
    private void SchedulingTable<T>(ModelBuilder m,string table)where T:SchedulingRecord
    {
        var e=m.Entity<T>();e.HasBaseType((Type?)null);e.ToTable(table);e.HasKey(x=>x.Id);e.HasOne<Organization>().WithMany().HasForeignKey(x=>x.OrganizationId).OnDelete(DeleteBehavior.Restrict);e.HasOne<Venue>().WithMany().HasForeignKey(x=>x.VenueId).OnDelete(DeleteBehavior.Restrict);
        e.HasQueryFilter(x=>currentUser.IsSuperAdmin||(currentUser.OrganizationId!=null&&x.OrganizationId==currentUser.OrganizationId&&Memberships.Any(a=>a.OrganizationId==x.OrganizationId&&a.UserId==currentUser.UserId&&(a.VenueId==null||a.VenueId==x.VenueId))));foreach(var p in e.Metadata.GetProperties())p.SetColumnName(new NpgsqlSnakeCaseNameTranslator().TranslateMemberName(p.Name));
    }

    private void ConfigurePilot(ModelBuilder model)
    {
        model.Entity<Organization>().Property(x=>x.IsDemo).HasColumnName("is_demo");
        PilotTable<OnboardingState>(model,"onboarding_states");PilotTable<StaffInvite>(model,"staff_invites");PilotTable<PilotFeedback>(model,"pilot_feedback");PilotTable<PilotError>(model,"pilot_errors");PilotTable<BookingWatch>(model,"booking_watches");
        model.Entity<OnboardingState>().HasIndex(x=>x.OrganizationId).IsUnique();model.Entity<BookingWatch>().HasIndex(x=>x.OrganizationId).IsUnique();model.Entity<StaffInvite>().HasIndex(x=>x.TokenHash).IsUnique();model.Entity<PilotFeedback>().HasIndex(x=>new{x.OrganizationId,x.CreatedAt});model.Entity<PilotError>().HasIndex(x=>new{x.OrganizationId,x.CreatedAt});
        model.Entity<PilotFeedback>().Property(x=>x.Text).HasMaxLength(4000);model.Entity<PilotFeedback>().Property(x=>x.PageUrl).HasMaxLength(500);
    }
    private void PilotTable<T>(ModelBuilder model,string table) where T:PilotRecord
    {
        var e=model.Entity<T>();e.HasBaseType((Type?)null);e.ToTable(table);e.HasKey(x=>x.Id);e.HasOne<Organization>().WithMany().HasForeignKey(x=>x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        e.HasQueryFilter(x=>currentUser.IsSuperAdmin||(currentUser.OrganizationId!=null&&x.OrganizationId==currentUser.OrganizationId));
        foreach(var p in e.Metadata.GetProperties())p.SetColumnName(new NpgsqlSnakeCaseNameTranslator().TranslateMemberName(p.Name));
    }

    private static string RoleName(OrganizationRole role) => role switch
    {
        OrganizationRole.OWNER => "Owner",
        OrganizationRole.MANAGER => "Manager",
        OrganizationRole.CASHIER => "Cashier",
        _ => role.ToString()
    };
}
