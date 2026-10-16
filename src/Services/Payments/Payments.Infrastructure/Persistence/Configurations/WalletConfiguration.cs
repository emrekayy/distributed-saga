using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.Domain.Wallets;

namespace Payments.Infrastructure.Persistence.Configurations;

internal sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("Wallets", t => t.HasCheckConstraint("CK_Wallets_Balance_NonNegative", "\"Balance\" >= 0"));
        builder.HasKey(w => w.CustomerId);
        builder.Property(w => w.CustomerId).ValueGeneratedNever();
        builder.Property(w => w.Currency).HasMaxLength(3).IsFixedLength();
    }
}
