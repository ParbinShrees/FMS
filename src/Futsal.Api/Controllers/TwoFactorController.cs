using Futsal.Api.Security;
using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Futsal.Domain.Identity;
namespace Futsal.Api.Controllers;
[ApiController,Route("api/v1/auth/two-factor"),Authorize(Policy="OwnerOnly"),Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
public sealed class TwoFactorController(FutsalDbContext db,ICurrentUser current,OwnerTwoFactor factor,PasswordHasher<AppUser> hasher,IClock clock):ControllerBase
{
 public sealed record SetupRequest(string Password);public sealed record VerifyRequest(string Code);
 [HttpPost("setup")]public async Task<IActionResult> Setup(SetupRequest input,CancellationToken ct)
 {
  var user=await db.Users.SingleAsync(x=>x.Id==current.UserId,ct);if(user.TotpEnabled||user.PasswordHash==null||hasher.VerifyHashedPassword(user,user.PasswordHash,input.Password)==PasswordVerificationResult.Failed)throw new ApiException(403,"FORBIDDEN","Reauthenticate with your password.");
  var secret=factor.NewSecret();user.TotpSecret=factor.Protect(secret);user.TotpLastStep=0;await db.SaveChangesAsync(ct);return Ok(new{Secret=secret,Uri=$"otpauth://totp/FutsalPro:{user.Id}?secret={secret}&issuer=FutsalPro&digits=6&period=30"});
 }
 [HttpPost("verify")]public async Task<IActionResult> Enable(VerifyRequest input,CancellationToken ct)
 {
  await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"totp:"+current.UserId}))",ct);
  var user=await db.Users.SingleAsync(x=>x.Id==current.UserId,ct);if(user.TotpEnabled||!factor.Verify(user,input.Code,false))throw new ApiException(400,"TWO_FACTOR_INVALID","Invalid verification code.");
  var codes=Enumerable.Range(0,10).Select(_=>Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12))).ToArray();user.RecoveryCodeHashes=codes.Select(OwnerTwoFactor.Hash).ToArray();user.TotpEnabled=true;
  db.AuditLogs.Add(new(){OrganizationId=current.OrganizationId,UserId=current.UserId,Action="TWO_FACTOR_ENABLED",EntityType="User",EntityId=user.Id,CreatedAt=clock.UtcNow});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Ok(new{RecoveryCodes=codes});
 }
}
