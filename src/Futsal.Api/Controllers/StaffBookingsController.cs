using FluentValidation;
using Futsal.Api.Bookings;
using Futsal.Api.Contracts;
using Futsal.Api.Identity;
using Futsal.Domain.Bookings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Futsal.Api.Controllers;

[ApiController,Route("api/v1/staff/bookings"),Authorize(Policy="StaffAccess")]
public sealed class StaffBookingsController(BookingService bookings,IValidator<StaffBookingRequest> staffValidator,IValidator<RescheduleBookingRequest> rescheduleValidator,IValidator<CancelBookingRequest> cancelValidator,IValidator<RecordPaymentRequest> paymentValidator):ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<BookingResponse>> Create(StaffBookingRequest request,[FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct)
    {
        await BookingsController.ValidateAsync(staffValidator,request,ct);BookingsController.RequireKey(key);
        var result=await bookings.CreateStaffBookingAsync(request,key!,HttpContext.Connection.RemoteIpAddress,ct);return StatusCode(StatusCodes.Status201Created,result);
    }
    [HttpPut("{id:guid}/reschedule")]
    public async Task<IActionResult> Reschedule(Guid id,RescheduleBookingRequest request,CancellationToken ct)
    {await BookingsController.ValidateAsync(rescheduleValidator,request,ct);await bookings.RescheduleAsync(id,request,HttpContext.Connection.RemoteIpAddress,ct);return NoContent();}
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id,CancelBookingRequest request,CancellationToken ct)
    {await BookingsController.ValidateAsync(cancelValidator,request,ct);await bookings.CancelStaffAsync(id,request.Reason,HttpContext.Connection.RemoteIpAddress,ct);return NoContent();}
    [HttpPost("{id:guid}/no-show")]
    public async Task<IActionResult> NoShow(Guid id,CancellationToken ct){await bookings.MarkNoShowAsync(id,HttpContext.Connection.RemoteIpAddress,ct);return NoContent();}
    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id,CancellationToken ct){await bookings.CompleteAsync(id,HttpContext.Connection.RemoteIpAddress,ct);return NoContent();}
    [HttpPost("{id:guid}/payments")]
    public async Task<IActionResult> Payment(Guid id,RecordPaymentRequest request,[FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct)
    {await BookingsController.ValidateAsync(paymentValidator,request,ct);BookingsController.RequireKey(key);await bookings.RecordPaymentAsync(id,request,key!,HttpContext.Connection.RemoteIpAddress,ct);return NoContent();}
    [HttpGet]
    public async Task<IActionResult> List([FromQuery]DateTimeOffset? from,[FromQuery]DateTimeOffset? to,[FromQuery]Guid? courtId,[FromQuery]BookingStatus? status,[FromQuery]Guid? customerId,[FromQuery]int page=1,[FromQuery]int pageSize=50,CancellationToken ct=default)
    {if(from is null||to is null)throw new ApiException(400,"VALIDATION_FAILED","from and to are required.");return Ok(await bookings.ListStaffAsync(from.Value,to.Value,courtId,status,customerId,page,pageSize,ct));}
}
