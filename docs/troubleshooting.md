# Bilinen tuzaklar

- .NET 10 SDK şablonları `-f net9.0` kabul etmez; `scaffold.sh` şablonları `-f`'siz üretir, `<TargetFramework>`,
  `<Nullable>`, `<ImplicitUsings>` satırlarını csproj'dan siler (merkezi props'tan gelir) ve CRLF/BOM'u temizler.
- MSBuild XML yorumlarında `--` kullanılamaz (props dosyası yüklenemez).
- Init script yalnızca **boş** postgres volume'unda çalışır; DB/kullanıcı değişikliği için `docker compose down -v`.
- **`.env` ve `AddIniFile`:** `PhysicalFileProvider` varsayılan olarak `.` ile başlayan dosyaları gizler; optional bir
  kaynak sessizce boş kalır. `ExclusionFilters.None` ile provider verilmeli (bkz. `ConfigurationExtensions`).
- **C# 14 (.NET 10 SDK, LangVersion=latest):** dizilerde `.Reverse()` artık `Span` üzerindeki in-place (void) metoda
  bağlanır; `Enumerable.Reverse(...)` kullanın.
- EF migration dosyaları block-scoped namespace ile üretilir; `.editorconfig`'te `Migrations/` `generated_code = true`.
  `scaffold.sh` normalizasyonu EF'in yazdığı BOM'u da temizler (zararsız).
- `dotnet ef` migration'ı ilk çalıştırmada `__EFMigrationsHistory` yok diye bir "Failed executing DbCommand" logu
  basar; beklenen davranıştır.
- MassTransit test harness: `Any()` beklemeleri inactivity timeout'a bağlı; yoklama için kayıtlı mesajları senkron
  okuyun (`WaitUntilAsync`). `Publish(message, ctx => …)` callback overload'ları `using MassTransit;` ister.
- RabbitMQ management API kuyruk istatistikleri ~5 sn gecikmeli; anlık sayım için `rabbitmqctl list_queues`.
