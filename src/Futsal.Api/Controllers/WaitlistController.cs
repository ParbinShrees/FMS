using Futsal.Api.Scheduling;
using Futsal.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Futsal.Api.Controllers;
[ApiController,Route("api/v1/waitlist"),Authorize(Roles="Customer")]
public sealed class WaitlistController(WaitlistService service):ControllerBase
{
 [HttpPost]public async Task<IActionResult> Join(WaitlistJoin input,[FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct){BookingsController.RequireKey(key);return Ok(await service.JoinAsync(input,key!,false,ct));}
 [HttpGet("mine")]public async Task<IActionResult> Mine(CancellationToken ct)=>Ok(await service.ListAsync(null,null,false,ct));
 [HttpPost("{id:guid}/claim")]public async Task<IActionResult> Claim(Guid id,CancellationToken ct)=>Ok(await service.ClaimAsync(id,ct));
 [HttpPost("{id:guid}/cancel")]public async Task<IActionResult> Cancel(Guid id,CancellationToken ct){await service.CancelAsync(id,false,ct);return NoContent();}
}
[ApiController,Route("api/v1/staff/waitlist"),Authorize(Policy="StaffAccess")]
public sealed class StaffWaitlistController(WaitlistService service):ControllerBase
{
 [HttpPost]public async Task<IActionResult> Join(WaitlistJoin input,[FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct){BookingsController.RequireKey(key);return Ok(await service.JoinAsync(input,key!,true,ct));}
 [HttpGet]public async Task<IActionResult> List(Guid? venueId,DateOnly? date,CancellationToken ct)=>Ok(await service.ListAsync(venueId,date,true,ct));
 [HttpPost("{id:guid}/cancel")]public async Task<IActionResult> Cancel(Guid id,CancellationToken ct){await service.CancelAsync(id,true,ct);return NoContent();}
 [HttpPost("{id:guid}/approve"),Authorize(Policy="CanManageStaff")]public async Task<IActionResult> Approve(Guid id,CancellationToken ct){await service.ApproveAsync(id,ct);return NoContent();}
 [HttpPost("{id:guid}/confirm")]public async Task<IActionResult> Confirm(Guid id,WaitlistConfirm input,[FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct){BookingsController.RequireKey(key);await service.ConfirmStaffAsync(id,input,key!,ct);return NoContent();}
}
