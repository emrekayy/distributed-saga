namespace Order.Infrastructure.Persistence;

/// <summary>
/// Quartz AdoJobStore tabloları ("quartz" şeması). Quartz tabloları kendisi oluşturmaz; şema EF migration'ı
/// üzerinden kurulur, böylece order_db'nin tek migration yolu vardır.
/// </summary>
internal static class QuartzSchema
{
    public const string Name = "quartz";

    public const string TablePrefix = Name + ".qrtz_";

    public static string CreateScript()
    {
        var assembly = typeof(QuartzSchema).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("quartz_tables_postgres.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
