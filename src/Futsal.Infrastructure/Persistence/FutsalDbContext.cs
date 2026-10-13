using Microsoft.EntityFrameworkCore;

namespace Futsal.Infrastructure.Persistence;

public sealed class FutsalDbContext(DbContextOptions<FutsalDbContext> options) : DbContext(options);
