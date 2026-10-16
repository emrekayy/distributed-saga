namespace Inventory.API;

/// <summary>
/// Entegrasyon testleri için <c>WebApplicationFactory&lt;T&gt;</c> işaretçisi. Üç API'nin de global namespace'te
/// bir <c>Program</c> sınıfı olduğundan, aynı test process'inde çakışmamak için her servis kendi tipini verir.
/// </summary>
public sealed class InventoryApiMarker;
