using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Shared.Contracts.Orders.Events;

namespace Shared.Contracts.Tests;

/// <summary>
/// Shared.Contracts'ın bağımlılıksız kalmasını ve mesaj isim/şekil kurallarını korur.
/// </summary>
public class ContractArchitectureTests
{
    private const string _rootNamespace = "Shared.Contracts";
    private const string _commonNamespace = _rootNamespace + ".Common";
    private const string _projectRelativePath = "src/BuildingBlocks/Shared.Contracts";

    private static readonly string[] _eventSuffixes =
        ["Reserved", "Completed", "Failed", "Released", "Submitted", "Refunded"];

    private static Assembly ContractsAssembly => typeof(OrderSubmitted).Assembly;

    private static IReadOnlyList<Type> PublicTypes => ContractsAssembly.GetExportedTypes();

    private static IReadOnlyList<Type> Events =>
        PublicTypes.Where(t => t.Namespace?.EndsWith(".Events", StringComparison.Ordinal) == true).ToList();

    private static IReadOnlyList<Type> Commands =>
        PublicTypes.Where(t => t.Namespace?.EndsWith(".Commands", StringComparison.Ordinal) == true).ToList();

    private static IReadOnlyList<Type> Messages => Events.Concat(Commands).ToList();

    [Fact]
    public void Project_has_no_package_or_project_references()
    {
        var root = FindRepositoryRoot();
        var projectDir = Path.Combine(root, _projectRelativePath);

        var csproj = XDocument.Load(Path.Combine(projectDir, "Shared.Contracts.csproj"));
        var references = csproj.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .ToList();
        Assert.Empty(references);

        // Restore sonucu: Directory.*.props üzerinden gelebilecek dolaylı paketleri de yakalar.
        var assetsPath = Path.Combine(projectDir, "obj", "project.assets.json");
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var libraries = assets.RootElement.GetProperty("libraries").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Empty(libraries);
    }

    [Fact]
    public void Assembly_references_only_the_framework()
    {
        var nonFramework = ContractsAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name != "System" && name != "netstandard" && name != "mscorlib"
                && !name.StartsWith("System.", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(nonFramework);
    }

    [Fact]
    public void Contracts_contain_events_and_commands()
    {
        Assert.NotEmpty(Events);
        Assert.NotEmpty(Commands);
    }

    [Fact]
    public void All_messages_are_public_sealed_records()
    {
        var violations = Messages
            .Where(t => !t.IsPublic || !t.IsSealed || !IsRecord(t))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Event_names_end_with_past_tense_suffix()
    {
        var violations = Events
            .Where(t => !_eventSuffixes.Any(s => t.Name.EndsWith(s, StringComparison.Ordinal)))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void No_type_name_ends_with_Event_or_Command()
    {
        var violations = PublicTypes
            .Where(t => t.Name.EndsWith("Event", StringComparison.Ordinal)
                || t.Name.EndsWith("Command", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Every_message_has_Guid_OrderId()
    {
        var violations = Messages
            .Where(t => t.GetProperty("OrderId")?.PropertyType != typeof(Guid))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Events_have_OccurredAt_and_commands_have_RequestedAt()
    {
        var violations = Events
            .Where(t => t.GetProperty("OccurredAt")?.PropertyType != typeof(DateTimeOffset))
            .Concat(Commands.Where(t => t.GetProperty("RequestedAt")?.PropertyType != typeof(DateTimeOffset)))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Public_non_message_types_live_only_in_Common()
    {
        var violations = PublicTypes
            .Except(Messages)
            .Where(t => t.Namespace != _commonNamespace)
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Message_namespaces_follow_area_and_kind_convention()
    {
        string[] areas = ["Orders", "Stock", "Payments"];
        var allowed = areas
            .SelectMany(a => new[] { $"{_rootNamespace}.{a}.Commands", $"{_rootNamespace}.{a}.Events" })
            .ToHashSet(StringComparer.Ordinal);

        var violations = Messages
            .Where(t => !allowed.Contains(t.Namespace!))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(violations);
    }

    // Derleyici record'lar için "<Clone>$" metodunu üretir; reflection'da record'u ayırt etmenin tek yolu.
    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null;

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DistributedSaga.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("DistributedSaga.sln bulunamadı.");
    }
}
