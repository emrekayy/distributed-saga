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
- **Migration eklendikten sonra `dotnet run --no-build`:** `dotnet ef migrations add` dosyaları üretir ama derlemez;
  yeniden `dotnet build` almadan çalıştırırsan "No migrations were found in assembly" görür ve tablolar oluşmaz.
- **`FOR UPDATE` ve izolasyon:** Pessimistic kilit kullanan servislerin outbox'ı ReadCommitted olmalı (ADR-020);
  RepeatableRead'de bekleyen transaction `40001` ile retry'a düşer.
- **Entegrasyon testleri hiç başlamıyor / takılıyor:** Testcontainers imaj çekmeden önce `docker-credential-desktop get`
  çağırır; etkileşimsiz ortamda (anahtarlık izni) bu süreç sonsuza kadar bekleyebilir. Çözüm: boş bir Docker config dizini
  verin: `mkdir -p /tmp/dockercfg && echo '{"auths":{}}' > /tmp/dockercfg/config.json` ve `DOCKER_CONFIG=/tmp/dockercfg dotnet test …`.
- **Entegrasyon testleri tek collection'dır:** Quartz scheduler adı process genelinde tekil (`order-service`), process'te tek
  Ordering host'u olabilir. Test host'ları `Testing` ortamında çalışır (`.env`, startup migration/seed yok); ayarlar bellek içi
  konfigürasyondan gelir, bu yüzden connection string'ler DI zamanında çözülür (yoksa override'lar geç kalır).
