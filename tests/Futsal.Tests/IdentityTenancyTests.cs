using System.Security.Claims;
using System.Security.Cryptography;
using Futsal.Api.Authorization;
using Futsal.Api.Contracts;
using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Futsal.Tests;

public sealed class IdentityTenancyTests
{
    [Fact]
    public async Task StaffLogin_SucceedsWithCorrectPassword()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (user, organization) = await fixture.CreateStaffAsync(OrganizationRole.OWNER);

        var session = await fixture.Auth.StaffLoginAsync(
            new StaffLoginRequest(user.Email!, fixture.Password, organization.Id), null, CancellationToken.None);

        Assert.Equal(user.Id, session.UserId);
        Assert.Equal(organization.Id, session.OrganizationId);
        Assert.Equal("Owner", session.Role);
        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
        var savedRefresh = await fixture.Database.RefreshTokens.SingleAsync();
        Assert.NotEqual(session.RefreshToken, savedRefresh.TokenHash);
        Assert.Equal(JwtTokenService.HashRefreshToken(session.RefreshToken), savedRefresh.TokenHash);
    }

    [Fact]
    public async Task StaffLogin_FailsWithWrongPassword()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (user, organization) = await fixture.CreateStaffAsync(OrganizationRole.OWNER);
        var wrongPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var exception = await Assert.ThrowsAsync<ApiException>(() => fixture.Auth.StaffLoginAsync(
            new StaffLoginRequest(user.Email!, wrongPassword, organization.Id), null, CancellationToken.None));

        Assert.Equal("INVALID_CREDENTIALS", exception.Code);
        Assert.Equal(1, (await fixture.Database.Users.SingleAsync(x => x.Id == user.Id)).FailedLoginCount);
        Assert.Contains(await fixture.Database.AuditLogs.IgnoreQueryFilters().ToListAsync(), x => x.Action == "LOGIN_FAILED");
    }

    [Fact]
    public async Task StaffAccount_LocksAfterFiveFailedAttempts()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (user, organization) = await fixture.CreateStaffAsync(OrganizationRole.OWNER);
        var wrongPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        ApiException? finalException = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            finalException = await Assert.ThrowsAsync<ApiException>(() => fixture.Auth.StaffLoginAsync(
                new StaffLoginRequest(user.Email!, wrongPassword, organization.Id), null, CancellationToken.None));
        }

        Assert.Equal("ACCOUNT_LOCKED", finalException!.Code);
        var lockedUser = await fixture.Database.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Equal(fixture.Clock.UtcNow.AddMinutes(15), lockedUser.LockedUntil);
        var correctPasswordWhileLocked = await Assert.ThrowsAsync<ApiException>(() => fixture.Auth.StaffLoginAsync(
            new StaffLoginRequest(user.Email!, fixture.Password, organization.Id), null, CancellationToken.None));
        Assert.Equal("ACCOUNT_LOCKED", correctPasswordWhileLocked.Code);
    }

    [Fact]
    public async Task RefreshToken_RotatesAndReuseRevokesActiveTokens()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (user, organization) = await fixture.CreateStaffAsync(OrganizationRole.MANAGER);
        var original = await fixture.Auth.StaffLoginAsync(
            new StaffLoginRequest(user.Email!, fixture.Password, organization.Id), null, CancellationToken.None);

        var rotated = await fixture.Auth.RefreshAsync(new RefreshRequest(original.RefreshToken, organization.Id), null, CancellationToken.None);

        Assert.NotEqual(original.RefreshToken, rotated.RefreshToken);
        var originalRecord = await fixture.Database.RefreshTokens.SingleAsync(x => x.UserId == user.Id && x.RevokedAt != null);
        Assert.NotNull(originalRecord.ReplacedBy);
        Assert.Contains(await fixture.Database.AuditLogs.IgnoreQueryFilters().ToListAsync(), x => x.Action == "TOKEN_ROTATED");

        var reused = await Assert.ThrowsAsync<ApiException>(() => fixture.Auth.RefreshAsync(
            new RefreshRequest(original.RefreshToken, organization.Id), null, CancellationToken.None));
        Assert.Equal("REFRESH_TOKEN_REUSED", reused.Code);
        Assert.All(await fixture.Database.RefreshTokens.Where(x => x.UserId == user.Id).ToListAsync(), token => Assert.NotNull(token.RevokedAt));
    }

    [Fact]
    public async Task OrganizationScope_PreventsCrossTenantReadsAndWrites()
    {
        var setup = await TestFixture.CreateAsync();
        var first = new Organization { Name = "Organization A", CreatedAt = setup.Clock.UtcNow };
        var second = new Organization { Name = "Organization B", CreatedAt = setup.Clock.UtcNow };
        var firstUser = new AppUser { Email = "a@example.test", FullName = "A", CreatedAt = setup.Clock.UtcNow };
        var secondUser = new AppUser { Email = "b@example.test", FullName = "B", CreatedAt = setup.Clock.UtcNow };
        var firstMembership = new Membership { OrganizationId = first.Id, UserId = firstUser.Id, Role = OrganizationRole.CASHIER };
        var secondMembership = new Membership { OrganizationId = second.Id, UserId = secondUser.Id, Role = OrganizationRole.OWNER };
        setup.Database.AddRange(first, second, firstUser, secondUser, firstMembership, secondMembership);
        await setup.Database.SaveChangesAsync();
        var databaseName = setup.DatabaseName;
        await setup.DisposeAsync();

        var currentUser = new FakeCurrentUser(firstUser.Id, first.Id, "Cashier", authenticated: true);
        await using var tenantDatabase = await TestFixture.CreateAsync(currentUser, databaseName);

        Assert.Equal(new[] { first.Id }, await tenantDatabase.Database.Organizations.Select(x => x.Id).ToArrayAsync());
        Assert.Equal(new[] { firstMembership.Id }, await tenantDatabase.Database.Memberships.Select(x => x.Id).ToArrayAsync());

        var leakedMembership = await tenantDatabase.Database.Memberships.IgnoreQueryFilters().SingleAsync(x => x.Id == secondMembership.Id);
        leakedMembership.Role = OrganizationRole.CASHIER;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tenantDatabase.Database.SaveChangesAsync());
        tenantDatabase.Database.Entry(leakedMembership).State = EntityState.Detached;

        var leakedOrganization = await tenantDatabase.Database.Organizations.IgnoreQueryFilters().SingleAsync(x => x.Id == second.Id);
        leakedOrganization.Name = "Changed by Organization A";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tenantDatabase.Database.SaveChangesAsync());
    }

    [Fact]
    public async Task Cashier_CannotUseOwnerOrManagerPolicies()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (user, organization) = await fixture.CreateStaffAsync(OrganizationRole.CASHIER);
        fixture.CurrentUser.Set(user.Id, organization.Id, "Cashier", authenticated: true);

        var canManageStaff = await fixture.AuthorizeAsync("CanManageStaff", "Cashier");
        var canViewReports = await fixture.AuthorizeAsync("CanViewReports", "Cashier");

        Assert.False(canManageStaff);
        Assert.False(canViewReports);
    }

    [Fact]
    public async Task Customer_CannotUseStaffPolicy()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var canAccessStaff = await fixture.AuthorizeAsync("StaffAccess", "Customer");

        Assert.False(canAccessStaff);
    }

    [Fact]
    public async Task MembershipRoleChange_WritesImmutableAuditEntry()
    {
        var setup = await TestFixture.CreateAsync();
        var (user, organization) = await setup.CreateStaffAsync(OrganizationRole.CASHIER);
        var databaseName = setup.DatabaseName;
        await setup.DisposeAsync();

        var currentUser = new FakeCurrentUser(user.Id, organization.Id, "Owner", authenticated: true);
        await using var tenantDatabase = await TestFixture.CreateAsync(currentUser, databaseName);
        var membership = await tenantDatabase.Database.Memberships.SingleAsync(x => x.UserId == user.Id);
        membership.Role = OrganizationRole.MANAGER;
        await tenantDatabase.Database.SaveChangesAsync();

        var audit = await tenantDatabase.Database.AuditLogs.SingleAsync(x => x.Action == "ROLE_CHANGED");
        Assert.Equal("{\"role\":\"Cashier\"}", audit.Before);
        Assert.Equal("{\"role\":\"Manager\"}", audit.After);
        audit.Action = "EDITED";
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenantDatabase.Database.SaveChangesAsync());
    }

    [Fact]
    public async Task CustomerOtp_IsHashedLimitedAndCreatesCustomerSession()
    {
        await using var fixture = await TestFixture.CreateAsync();
        const string phone = "+9779800000000";
        await fixture.Auth.RequestOtpAsync(new OtpRequest(phone), CancellationToken.None);
        var storedOtp = await fixture.Database.OtpCodes.SingleAsync();
        var code = fixture.OtpSender.LastCode!;
        var wrongCode = code == "000000" ? "000001" : "000000";
        Assert.NotEqual(code, storedOtp.CodeHash);
        Assert.Equal(6, code.Length);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<ApiException>(() => fixture.Auth.VerifyOtpAsync(
                new OtpVerifyRequest(phone, wrongCode), null, CancellationToken.None));
        }

        var rejected = await Assert.ThrowsAsync<ApiException>(() => fixture.Auth.VerifyOtpAsync(
            new OtpVerifyRequest(phone, code), null, CancellationToken.None));
        Assert.Equal("OTP_INVALID", rejected.Code);

        await fixture.Auth.RequestOtpAsync(new OtpRequest(phone), CancellationToken.None);
        var customerCode = fixture.OtpSender.LastCode!;
        var session = await fixture.Auth.VerifyOtpAsync(
            new OtpVerifyRequest(phone, customerCode, "OTP Customer"), null, CancellationToken.None);
        Assert.Equal("Customer", session.Role);
        Assert.Null(session.OrganizationId);
        Assert.Equal("OTP Customer", (await fixture.Database.Users.SingleAsync(x => x.Phone == phone)).FullName);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly DbContextOptions<FutsalDbContext> _options;
        public string DatabaseName { get; private set; } = Guid.NewGuid().ToString("N");
        public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        public string Password { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        public FakeCurrentUser CurrentUser { get; }
        public FutsalDbContext Database { get; }
        public AuthService Auth { get; }
        public NoopOtpSender OtpSender { get; }

        private TestFixture(FakeCurrentUser currentUser, string? databaseName)
        {
            CurrentUser = currentUser;
            if (databaseName is not null) DatabaseName = databaseName;
            _options = new DbContextOptionsBuilder<FutsalDbContext>()
                .UseInMemoryDatabase(DatabaseName)
                .Options;
            Database = new FutsalDbContext(_options, CurrentUser, Clock);
            var passwordHasher = new PasswordHasher<AppUser>();
            var otpHasher = new PasswordHasher<OtpCode>();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                ["Jwt:Issuer"] = "tests",
                ["Jwt:Audience"] = "tests"
            }).Build();
            var tokenService = new JwtTokenService(config, Clock);
            OtpSender = new NoopOtpSender();
            Auth = new AuthService(Database, passwordHasher, otpHasher, tokenService, OtpSender, Clock, NullLogger<AuthService>.Instance);
        }

        public static async Task<TestFixture> CreateAsync(FakeCurrentUser? currentUser = null, string? databaseName = null)
        {
            var fixture = new TestFixture(currentUser ?? new FakeCurrentUser(), databaseName);
            await fixture.Database.Database.EnsureCreatedAsync();
            return fixture;
        }

        public async Task<(AppUser User, Organization Organization)> CreateStaffAsync(OrganizationRole role)
        {
            var organization = new Organization { Name = $"Organization {Guid.NewGuid():N}", CreatedAt = Clock.UtcNow };
            var user = new AppUser { Email = $"{Guid.NewGuid():N}@example.test", FullName = "Test Staff", CreatedAt = Clock.UtcNow };
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, Password);
            Database.AddRange(organization, user, new Membership { OrganizationId = organization.Id, UserId = user.Id, Role = role });
            await Database.SaveChangesAsync();
            return (user, organization);
        }

        public async Task<bool> AuthorizeAsync(string policyName, string role)
        {
            CurrentUser.Set(CurrentUser.UserId ?? Guid.NewGuid(), CurrentUser.OrganizationId, role, authenticated: true);
            var requirement = policyName switch
            {
                "CanManageStaff" => new OrganizationRoleRequirement(OrganizationRole.OWNER, OrganizationRole.MANAGER),
                "CanViewReports" => new OrganizationRoleRequirement(OrganizationRole.OWNER, OrganizationRole.MANAGER),
                _ => new OrganizationRoleRequirement(OrganizationRole.OWNER, OrganizationRole.MANAGER, OrganizationRole.CASHIER)
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));
            var context = new AuthorizationHandlerContext([requirement], principal, null);
            await new OrganizationRoleHandler(Database, CurrentUser).HandleAsync(context);
            return context.HasSucceeded;
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class FakeCurrentUser(Guid? userId = null, Guid? organizationId = null, string? role = null, bool authenticated = false) : ICurrentUser
    {
        public Guid? UserId { get; private set; } = userId;
        public Guid? OrganizationId { get; private set; } = organizationId;
        public string? Role { get; private set; } = role;
        public bool IsSuperAdmin { get; private set; }
        public bool IsAuthenticated { get; private set; } = authenticated;

        public void Set(Guid? userId, Guid? organizationId, string? role, bool authenticated, bool isSuperAdmin = false)
        {
            UserId = userId;
            OrganizationId = organizationId;
            Role = role;
            IsAuthenticated = authenticated;
            IsSuperAdmin = isSuperAdmin;
        }
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class NoopOtpSender : IOtpSender
    {
        public string? LastCode { get; private set; }
        public Task SendAsync(string phone, string code, CancellationToken cancellationToken)
        {
            LastCode = code;
            return Task.CompletedTask;
        }
    }
}
