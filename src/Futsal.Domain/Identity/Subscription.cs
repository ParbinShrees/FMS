namespace Futsal.Domain.Identity;
public sealed class Subscription:IOrganizationScoped
{
 public Guid Id{get;set;}=Guid.NewGuid();public Guid OrganizationId{get;set;}
 public string Plan{get;set;}="Basic";public string Status{get;set;}="TRIAL";
 public DateTimeOffset StartsAt{get;set;}public DateTimeOffset? EndsAt{get;set;}
 public int MaxCourts{get;set;}=2;public int GraceDays{get;set;}=7;
 public void Check(DateTimeOffset now){if(Status is "TRIAL" or "ACTIVE" && EndsAt<=now)Status="PAST_DUE";}
 public bool ReadOnly(DateTimeOffset now)=>Status=="CANCELLED"||(Status=="PAST_DUE"&&EndsAt==null)||StartsAt>now||EndsAt?.AddDays(GraceDays)<=now;
 public void Validate(){if(!new[]{"TRIAL","ACTIVE","PAST_DUE","CANCELLED"}.Contains(Status)||MaxCourts<1||GraceDays is <0 or >90||EndsAt<=StartsAt)throw new ArgumentException("Invalid subscription settings.");}
}
