using Microsoft.EntityFrameworkCore;

namespace WebHealth.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options);
