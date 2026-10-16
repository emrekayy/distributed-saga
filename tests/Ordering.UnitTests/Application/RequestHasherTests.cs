using Ordering.Application.Orders;

namespace Ordering.UnitTests.Application;

public class RequestHasherTests
{
    private static readonly Guid _customer = Guid.Parse("6f1c2b8e-3a4d-4f5e-9a7b-1c2d3e4f5a6b");
    private static readonly SubmitOrderItem _a = new(Guid.Parse("a1a1a1a1-0000-0000-0000-000000000001"), 2, 150.00m);
    private static readonly SubmitOrderItem _b = new(Guid.Parse("a1a1a1a1-0000-0000-0000-000000000002"), 1, 99.90m);

    [Fact]
    public void Hash_is_independent_of_item_order()
    {
        Assert.Equal(Hash([_a, _b]), Hash([_b, _a]));
    }

    [Fact]
    public void Hash_ignores_decimal_trailing_zeros()
    {
        Assert.Equal(Hash([_a with { UnitPrice = 150m }]), Hash([_a with { UnitPrice = 150.000m }]));
    }

    [Fact]
    public void Hash_ignores_idempotency_key()
    {
        Assert.Equal(Hash([_a], key: "k1"), Hash([_a], key: "k2"));
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("price")]
    [InlineData("currency")]
    [InlineData("customer")]
    [InlineData("items")]
    public void Hash_changes_when_body_changes(string change)
    {
        var baseline = Hash([_a, _b]);

        var changed = change switch
        {
            "quantity" => Hash([_a with { Quantity = 3 }, _b]),
            "price" => Hash([_a with { UnitPrice = 150.01m }, _b]),
            "currency" => Hash([_a, _b], currency: "EUR"),
            "customer" => Hash([_a, _b], customer: Guid.NewGuid()),
            _ => Hash([_a]),
        };

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void Hash_is_lowercase_sha256_hex()
    {
        Assert.Matches("^[0-9a-f]{64}$", Hash([_a]));
    }

    private static string Hash(SubmitOrderItem[] items, string currency = "TRY", Guid? customer = null, string key = "k") =>
        RequestHasher.Compute(new SubmitOrderCommand(customer ?? _customer, currency, items, key));
}
