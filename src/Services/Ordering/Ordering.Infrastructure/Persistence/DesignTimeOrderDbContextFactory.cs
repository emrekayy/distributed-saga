using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ordering.Infrastructure.Persistence;

/// <summary>
/// "dotnet ef migrations add" için. Migration üretmek veritabanı bağlantısı gerektirmez;
/// connection string yalnızca provider'ı seçmek için verilir.
/// </summary>
internal sealed class DesignTimeOrderDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql("Host=localhost;Database=order_db")
            .Options);
}
