using DotNet.Testcontainers.Configurations;
using System.Net;
using System.Net.Sockets;
using Inventory.API;
using Inventory.Domain.Products;
using Inventory.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using Ordering.API;
using Ordering.API.Endpoints;
using Ordering.Infrastructure.Persistence;
using Payments.API;
using Payments.Domain.Wallets;
using Payments.Infrastructure.FaultInjection;
using Payments.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace IntegrationTests.Support;

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Name = "integration";
}

/// <summary>
/// Collection başına bir kez: Postgres (repo'nun init script'iyle, servis başına ayrı kullanıcı) + RabbitMQ
/// container'ları, üç DB'nin migration'ı, üç API (in-process) ve test bus'ı. Quartz scheduler adı process genelinde
/// tekil olduğu için process'te tek Ordering host'u vardır; tüm entegrasyon testleri bu tek collection'dadır.
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime
{
    public const string Vhost = "saga";

    /// <summary>Saga ödeme timeout'u; timeout testleri bu süreyi bekler, diğerleri hiç tetiklemez.</summary>
    public static readonly TimeSpan PaymentTimeout = TimeSpan.FromSeconds(6);

    private const string AdminUser = "saga_admin";
    private const string AdminPassword = "Test_Admin_123";
    private const string RabbitUser = "saga";
    private const string RabbitPassword = "Test_Rabbit_123";

    private PostgreSqlContainer _postgres = null!;
    private RabbitMqContainer _rabbit = null!;
    private int _rabbitPort;

    private ServiceFactory<OrderingApiMarker> _ordering = null!;
    private ServiceFactory<InventoryApiMarker> _inventory = null!;
    private ServiceFactory<PaymentsApiMarker> _payments = null!;

    public MessageProbe Probe { get; private set; } = null!;

    public ConsumeActivityObserver InventoryConsumers { get; } = new();

    public ConsumeActivityObserver PaymentsConsumers { get; } = new();

    public DbFailureLog InventoryDbFailures { get; } = new();

    public DbFailureLog PaymentsDbFailures { get; } = new();

    public HttpClient OrderingClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _rabbitPort = FreeTcpPort();

        _postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithUsername(AdminUser)
            .WithPassword(AdminPassword)
            .WithEnvironment("ORDER_DB_NAME", "order_db")
            .WithEnvironment("ORDER_DB_USER", "order_svc")
            .WithEnvironment("ORDER_DB_PASSWORD", "Test_Order_123")
            .WithEnvironment("STOCK_DB_NAME", "stock_db")
            .WithEnvironment("STOCK_DB_USER", "stock_svc")
            .WithEnvironment("STOCK_DB_PASSWORD", "Test_Stock_123")
            .WithEnvironment("PAYMENT_DB_NAME", "payment_db")
            .WithEnvironment("PAYMENT_DB_USER", "payment_svc")
            .WithEnvironment("PAYMENT_DB_PASSWORD", "Test_Payment_123")
            .WithBindMount(RepoFile("deploy/postgres/init/01-init-databases.sh"), "/docker-entrypoint-initdb.d/01-init-databases.sh", AccessMode.ReadOnly)
            .WithCommand("-c", "max_connections=300")
            .Build();

        // Sabit host port: broker container'ı durdurulup başlatılınca adres değişmez (outbox testi).
        _rabbit = new RabbitMqBuilder("rabbitmq:4.1-management-alpine")
            .WithUsername(RabbitUser)
            .WithPassword(RabbitPassword)
            .WithEnvironment("RABBITMQ_DEFAULT_VHOST", Vhost)
            .WithPortBinding(_rabbitPort, 5672)
            .Build();

        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

        await MigrateAsync();

        var settings = BuildSettings();
        _ordering = new ServiceFactory<OrderingApiMarker>(settings);
        _inventory = new ServiceFactory<InventoryApiMarker>(settings, s =>
        {
            s.AddConsumeObserver(_ => InventoryConsumers);
            s.ConfigureDbContext<InventoryDbContext>(InventoryDbFailures.Attach);
        });
        _payments = new ServiceFactory<PaymentsApiMarker>(settings, s =>
        {
            s.AddConsumeObserver(_ => PaymentsConsumers);
            s.ConfigureDbContext<PaymentDbContext>(PaymentsDbFailures.Attach);
        });

        // Host'lar (MassTransit bus, Quartz) CreateClient ile başlar.
        OrderingClient = _ordering.CreateClient();
        _ = _inventory.CreateClient();
        _ = _payments.CreateClient();

        Probe = await MessageProbe.StartAsync(_rabbit.Hostname, (ushort)_rabbitPort, Vhost, RabbitUser, RabbitPassword);
    }

    public async Task DisposeAsync()
    {
        if (Probe is not null)
        {
            await Probe.DisposeAsync();
        }

        OrderingClient?.Dispose();
        if (_ordering is not null)
        {
            await _ordering.DisposeAsync();
        }

        if (_inventory is not null)
        {
            await _inventory.DisposeAsync();
        }

        if (_payments is not null)
        {
            await _payments.DisposeAsync();
        }

        if (_rabbit is not null)
        {
            await _rabbit.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    // --- Veritabanı erişimi -------------------------------------------------------------------------------------

    public string OrderConnectionString => ConnectionString("order_db", "order_svc", "Test_Order_123");

    public string StockConnectionString => ConnectionString("stock_db", "stock_svc", "Test_Stock_123");

    public string PaymentConnectionString => ConnectionString("payment_db", "payment_svc", "Test_Payment_123");

    public OrderDbContext CreateOrderDb() =>
        new(new DbContextOptionsBuilder<OrderDbContext>().UseNpgsql(OrderConnectionString).Options);

    public InventoryDbContext CreateStockDb() =>
        new(new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql(StockConnectionString).Options);

    public PaymentDbContext CreatePaymentDb() =>
        new(new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql(PaymentConnectionString).Options);

    /// <summary>
    /// pg_stat_database.deadlocks (kümülatif). İstatistikler gecikmeli yazıldığı için okumadan önce bu bağlantının
    /// payı flush edilir ve snapshot temizlenir.
    /// </summary>
    public async Task<long> DeadlockCountAsync(string database)
    {
        await using var connection = new Npgsql.NpgsqlConnection(
            $"Host={_postgres.Hostname};Port={_postgres.GetMappedPublicPort(5432)};Database=postgres;Username={AdminUser};Password={AdminPassword};Pooling=false");
        await connection.OpenAsync();

        await using (var prepare = new Npgsql.NpgsqlCommand("SELECT pg_stat_force_next_flush(); SELECT pg_stat_clear_snapshot();", connection))
        {
            await prepare.ExecuteNonQueryAsync();
        }

        await using var query = new Npgsql.NpgsqlCommand("SELECT deadlocks FROM pg_stat_database WHERE datname = @db", connection);
        query.Parameters.AddWithValue("db", database);
        return (long)(await query.ExecuteScalarAsync() ?? 0L);
    }

    /// <summary>Verilen veritabanında kilit bekleyen backend sayısı (pg_stat_activity).</summary>
    public async Task<long> LockWaitersAsync(string database)
    {
        await using var connection = new Npgsql.NpgsqlConnection(
            $"Host={_postgres.Hostname};Port={_postgres.GetMappedPublicPort(5432)};Database=postgres;Username={AdminUser};Password={AdminPassword};Pooling=false");
        await connection.OpenAsync();
        await using var query = new Npgsql.NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = @db AND wait_event_type = 'Lock'", connection);
        query.Parameters.AddWithValue("db", database);
        return (long)(await query.ExecuteScalarAsync() ?? 0L);
    }

    /// <summary>MassTransit inbox kaydı sayısı (aynı MessageId için tek kayıt olmalı).</summary>
    public async Task<long> InboxCountAsync(string connectionString, Guid messageId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var query = new Npgsql.NpgsqlCommand("SELECT count(*) FROM \"InboxState\" WHERE \"MessageId\" = @id", connection);
        query.Parameters.AddWithValue("id", messageId);
        return (long)(await query.ExecuteScalarAsync() ?? 0L);
    }

    /// <summary>Tablodaki CHECK kısıtı sayısı (stok/bakiye negatife düşemez garantisinin asıl kaynağı).</summary>
    public async Task<long> CheckConstraintCountAsync(string connectionString, string table)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var query = new Npgsql.NpgsqlCommand(
            "SELECT count(*) FROM pg_constraint WHERE contype = 'c' AND conrelid = @t::regclass", connection);
        query.Parameters.AddWithValue("t", $"\"{table}\"");
        return (long)(await query.ExecuteScalarAsync() ?? 0L);
    }

    public async Task<Guid> SeedProductAsync(int quantity)
    {
        await using var db = CreateStockDb();
        var product = new Product(Guid.NewGuid(), $"Test product {Guid.NewGuid():N}", quantity);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    public async Task<Guid> SeedWalletAsync(decimal balance, string currency = "TRY")
    {
        await using var db = CreatePaymentDb();
        var wallet = new Wallet(Guid.NewGuid(), currency, balance);
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();
        return wallet.CustomerId;
    }

    // --- Sipariş API'si -----------------------------------------------------------------------------------------

    public async Task<Guid> SubmitOrderAsync(Guid customerId, params (Guid ProductId, int Quantity, decimal UnitPrice)[] items)
    {
        var request = new SubmitOrderRequest(
            customerId, "TRY", items.Select(i => new SubmitOrderItemRequest(i.ProductId, i.Quantity, i.UnitPrice)).ToList());
        using var message = new HttpRequestMessage(HttpMethod.Post, "/orders") { Content = JsonContent.Create(request) };
        message.Headers.Add(OrderEndpoints.IdempotencyKeyHeader, Guid.NewGuid().ToString());

        using var response = await OrderingClient.SendAsync(message);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!.Id;
    }

    public async Task<OrderResponse> GetOrderAsync(Guid orderId) =>
        (await OrderingClient.GetFromJsonAsync<OrderResponse>($"/orders/{orderId}"))!;

    /// <summary>Sipariş verilen duruma ulaşana kadar yoklar (zaman aşımı Eventually'de).</summary>
    public async Task<OrderResponse> WaitForOrderAsync(Guid orderId, string status, TimeSpan? timeout = null)
    {
        OrderResponse? last = null;
        await Eventually.UntilAsync(
            async () => (last = await GetOrderAsync(orderId)).Status == status,
            $"order {orderId} to become {status}",
            timeout);
        return last!;
    }

    // --- Hata enjeksiyonu (Payments) ------------------------------------------------------------------------------

    public FaultInjectionFilter PaymentsFaultFilter => _payments.Services.GetRequiredService<FaultInjectionFilter>();

    /// <summary>FaultInjection ayarlarını çalışma anında açar; dönen nesne Dispose edilince kapatır.</summary>
    public IDisposable InjectPaymentFaults(
        double transientFailureRate = 0, int maxTransientFailuresPerMessage = 3, bool permanentFailure = false, TimeSpan? delay = null)
    {
        SetPaymentFaults(true, transientFailureRate, maxTransientFailuresPerMessage, permanentFailure, delay ?? TimeSpan.Zero);
        return new FaultScope(() => SetPaymentFaults(false, 0, 3, false, TimeSpan.Zero));
    }

    private void SetPaymentFaults(bool enabled, double rate, int max, bool permanent, TimeSpan delay)
    {
        var services = _payments.Services;
        var configuration = services.GetRequiredService<IConfiguration>();
        configuration["FaultInjection:Enabled"] = enabled.ToString();
        configuration["FaultInjection:TransientFailureRate"] = rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
        configuration["FaultInjection:MaxTransientFailuresPerMessage"] = max.ToString(System.Globalization.CultureInfo.InvariantCulture);
        configuration["FaultInjection:PermanentFailure"] = permanent.ToString();
        configuration["FaultInjection:Delay"] = delay.ToString("c", System.Globalization.CultureInfo.InvariantCulture);

        // Monitor seçenekleri önbellekte tutar; temizlemek bir sonraki okumada güncel konfigürasyondan yeniden kurar.
        services.GetRequiredService<IOptionsMonitorCache<FaultInjectionOptions>>().Clear();
    }

    private sealed class FaultScope(Action reset) : IDisposable
    {
        public void Dispose() => reset();
    }

    // --- Broker ------------------------------------------------------------------------------------------------

    /// <summary>Kuyruktaki hazır mesaj sayısı (rabbitmqctl; management API ~5 sn gecikmeli olduğundan kullanılmaz).</summary>
    public async Task<int> QueueMessagesAsync(string queueName)
    {
        var result = await _rabbit.ExecAsync(["rabbitmqctl", "-q", "list_queues", "-p", Vhost, "name", "messages"]);
        var line = result.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\t'))
            .FirstOrDefault(parts => parts.Length == 2 && parts[0].Trim() == queueName);
        return line is null ? 0 : int.Parse(line[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    // --- Yardımcılar ---------------------------------------------------------------------------------------------

    private async Task MigrateAsync()
    {
        await using (var order = CreateOrderDb())
        {
            await order.Database.MigrateAsync();
        }

        await using (var stock = CreateStockDb())
        {
            await stock.Database.MigrateAsync();
        }

        await using (var payment = CreatePaymentDb())
        {
            await payment.Database.MigrateAsync();
        }
    }

    private Dictionary<string, string?> BuildSettings() => new()
    {
        ["POSTGRES_HOST"] = _postgres.Hostname,
        ["POSTGRES_PORT"] = _postgres.GetMappedPublicPort(5432).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["ORDER_DB_NAME"] = "order_db",
        ["ORDER_DB_USER"] = "order_svc",
        ["ORDER_DB_PASSWORD"] = "Test_Order_123",
        ["STOCK_DB_NAME"] = "stock_db",
        ["STOCK_DB_USER"] = "stock_svc",
        ["STOCK_DB_PASSWORD"] = "Test_Stock_123",
        ["PAYMENT_DB_NAME"] = "payment_db",
        ["PAYMENT_DB_USER"] = "payment_svc",
        ["PAYMENT_DB_PASSWORD"] = "Test_Payment_123",
        ["RABBITMQ_HOST"] = _rabbit.Hostname,
        ["RABBITMQ_AMQP_PORT"] = _rabbitPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["RABBITMQ_VHOST"] = Vhost,
        ["RABBITMQ_USER"] = RabbitUser,
        ["RABBITMQ_PASSWORD"] = RabbitPassword,
        ["OrderSaga:PaymentTimeout"] = PaymentTimeout.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
    };

    private string ConnectionString(string database, string user, string password) =>
        $"Host={_postgres.Hostname};Port={_postgres.GetMappedPublicPort(5432)};Database={database};Username={user};Password={password}";

    private static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string RepoFile(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{relativePath} not found above {AppContext.BaseDirectory}.");
    }
}
