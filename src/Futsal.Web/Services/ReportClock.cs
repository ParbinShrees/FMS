using Futsal.Application.Abstractions;
namespace Futsal.Web.Services;

public sealed class ReportClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
