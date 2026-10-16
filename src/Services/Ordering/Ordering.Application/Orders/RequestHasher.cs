using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ordering.Application.Orders;

/// <summary>
/// İsteğin kanonik JSON'unun SHA-256'sı. Kanonik form: sabit alan sırası, kalemler ProductId'ye göre
/// sıralı, decimal'ler sondaki sıfırlar atılmış invariant metin. Böylece kalem sırası ve "10.0"/"10.00"
/// gibi gösterim farkları hash'i değiştirmez. Idempotency-Key hash'e dahil değildir.
/// </summary>
public static class RequestHasher
{
    public static string Compute(SubmitOrderCommand command)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("customerId", command.CustomerId);
            writer.WriteString("currency", command.Currency);
            writer.WriteStartArray("items");
            foreach (var item in (command.Items ?? []).OrderBy(i => i.ProductId).ThenBy(i => i.Quantity).ThenBy(i => i.UnitPrice))
            {
                writer.WriteStartObject();
                writer.WriteString("productId", item.ProductId);
                writer.WriteNumber("quantity", item.Quantity);
                writer.WriteString("unitPrice", Normalize(item.UnitPrice));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    // "G29" decimal'in sondaki sıfırlarını atar: 10.00m -> "10".
    private static string Normalize(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}
