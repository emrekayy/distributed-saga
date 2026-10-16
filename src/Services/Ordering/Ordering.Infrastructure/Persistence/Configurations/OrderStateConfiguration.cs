using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering.Application.Saga;
using Shared.Contracts.Common;

namespace Ordering.Infrastructure.Persistence.Configurations;

internal sealed class OrderStateConfiguration : IEntityTypeConfiguration<OrderState>
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<OrderState> builder)
    {
        builder.ToTable("OrderState");
        builder.HasKey(x => x.CorrelationId);
        builder.Property(x => x.CorrelationId).ValueGeneratedNever();

        builder.Property(x => x.CurrentState).HasMaxLength(64);
        builder.HasIndex(x => x.CurrentState);

        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(x => x.TotalAmount).HasPrecision(19, 4);
        builder.Property(x => x.FailureReasonCode).HasMaxLength(64);
        builder.Property(x => x.FailureReason).HasMaxLength(1000);

        builder.Property(x => x.Items)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, _json),
                v => JsonSerializer.Deserialize<List<OrderItemDto>>(v, _json) ?? new List<OrderItemDto>(),
                new ValueComparer<List<OrderItemDto>>(
                    (a, b) => JsonSerializer.Serialize(a, _json) == JsonSerializer.Serialize(b, _json),
                    v => JsonSerializer.Serialize(v, _json).GetHashCode(StringComparison.Ordinal),
                    v => JsonSerializer.Deserialize<List<OrderItemDto>>(JsonSerializer.Serialize(v, _json), _json)!));

        // Optimistic concurrency: Postgres xmin.
        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}
