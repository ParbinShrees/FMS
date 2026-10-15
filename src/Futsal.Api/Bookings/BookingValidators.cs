using System.Text.RegularExpressions;
using FluentValidation;
using Futsal.Api.Contracts;

namespace Futsal.Api.Bookings;

public sealed class CreateHoldRequestValidator : AbstractValidator<CreateHoldRequest>
{
    public CreateHoldRequestValidator(){RuleFor(x=>x.CourtId).NotEmpty();RuleFor(x=>x.StartAt).NotEmpty();RuleFor(x=>x.EndAt).GreaterThan(x=>x.StartAt);}
}
public sealed class StaffBookingRequestValidator : AbstractValidator<StaffBookingRequest>
{
    public StaffBookingRequestValidator(){Include(new CreateHoldRequestValidatorAdapter());RuleFor(x=>x.Phone).NotEmpty();RuleFor(x=>x.FullName).NotEmpty().MaximumLength(160);RuleFor(x=>x.InitialPayment).GreaterThanOrEqualTo(0).When(x=>x.InitialPayment.HasValue);RuleFor(x=>x.InitialPaymentMethod).NotNull().When(x=>x.InitialPayment is > 0);}
    private sealed class CreateHoldRequestValidatorAdapter : AbstractValidator<StaffBookingRequest>{public CreateHoldRequestValidatorAdapter(){RuleFor(x=>x.CourtId).NotEmpty();RuleFor(x=>x.StartAt).NotEmpty();RuleFor(x=>x.EndAt).GreaterThan(x=>x.StartAt);}}
}
public sealed class RescheduleBookingRequestValidator : AbstractValidator<RescheduleBookingRequest>{public RescheduleBookingRequestValidator()=>RuleFor(x=>x.EndAt).GreaterThan(x=>x.StartAt);}
public sealed class CancelBookingRequestValidator : AbstractValidator<CancelBookingRequest>{public CancelBookingRequestValidator()=>RuleFor(x=>x.Reason).MaximumLength(500);}
public sealed class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>{public RecordPaymentRequestValidator(){RuleFor(x=>x.Amount).NotEqual(0);RuleFor(x=>x.Reference).MaximumLength(200);RuleFor(x=>x.Method).IsInEnum();}}

public static class NepaliPhone
{
    public static string Normalize(string value)
    {
        var digits=new string(value.Where(char.IsDigit).ToArray());
        if(digits.StartsWith("977")&&digits.Length==13)digits=digits[3..];
        if(digits.Length==10&&digits[0]=='9')digits="+977"+digits;
        else if(value.TrimStart().StartsWith('+')&&digits.Length==13&&digits.StartsWith("977"))digits="+"+digits;
        if (!Futsal.Application.Notifications.SmsText.ValidPhone(digits)) throw new Futsal.Api.Identity.ApiException(400, "VALIDATION_FAILED", "Phone must be a Nepal mobile number in E.164 format.");
        return digits;
    }
}
