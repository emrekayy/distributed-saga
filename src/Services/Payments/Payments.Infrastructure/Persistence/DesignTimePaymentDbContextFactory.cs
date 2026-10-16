using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Payments.Infrastructure.Persistence;

/// <summary>"dotnet ef migrations add" için; migration üretmek veritabanı bağlantısı gerektirmez.</summary>
internal sealed class DesignTimePaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql("Host=localhost;Database=payment_db")
            .Options);
}
