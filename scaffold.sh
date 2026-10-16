#!/usr/bin/env bash
# =============================================================================
#  Solution iskeletini dotnet CLI ile oluşturur. Repo kökünde çalıştırın:
#     chmod +x scaffold.sh && ./scaffold.sh
#  Gereksinim: .NET 9 veya üzeri SDK (projeler net9.0 hedefler).
#
#  Tekrar çalıştırılabilir (idempotent): var olan projeler yeniden oluşturulmaz,
#  sln/reference eklemeleri zaten varsa atlanır. macOS bash 3.2 + BSD araçlarıyla
#  uyumludur.
#
#  Not: Hedef framework (net9.0) Directory.Build.props'tan gelir. Şablonlara
#  -f verilmez; .NET 10 SDK şablonları "-f net9.0" seçeneğini kabul etmez.
#  Şablonun yazdığı <TargetFramework> satırı aşağıda csproj'dan silinir.
# =============================================================================
set -euo pipefail

cd "$(dirname "$0")"

SLN="DistributedSaga"
SERVICES=("Ordering" "Inventory" "Payments")

# new_project <şablon> <ad> <dizin> [ek şablon argümanları...]
# Proje zaten varsa dokunmaz (geliştirilmiş kodun üzerine yazmamak için).
new_project() {
  local template="$1" name="$2" dir="$3"
  shift 3
  if [ -f "$dir/$name.csproj" ]; then
    echo "   = $name zaten var, atlanıyor"
    return 0
  fi
  dotnet new "$template" -n "$name" -o "$dir" --no-restore "$@"
  # Yalnızca yeni oluşturulan projedeki şablon artıklarını temizle
  rm -f "$dir/Class1.cs" "$dir/UnitTest1.cs"
}

echo ">> Solution: $SLN"
if [ ! -f "$SLN.sln" ]; then
  dotnet new sln -n "$SLN" --format sln
fi
if [ ! -f .gitignore ]; then
  dotnet new gitignore >/dev/null
fi
grep -qxF ".env" .gitignore || printf "\n# Yerel secret'lar\n.env\n" >> .gitignore

# --- Building Blocks ---------------------------------------------------------
# Shared.Contracts       : yalnızca event/command sözleşmeleri, SIFIR bağımlılık
# BuildingBlocks.Messaging: MassTransit/RabbitMQ/Outbox ortak konfigürasyonu
new_project classlib Shared.Contracts         src/BuildingBlocks/Shared.Contracts
new_project classlib BuildingBlocks.Messaging src/BuildingBlocks/BuildingBlocks.Messaging
# BuildingBlocks.Web     : ortak ASP.NET Core parçaları (health endpoint'leri, ProblemDetails tabanı);
#                          yalnızca FrameworkReference, NuGet paketi yok
new_project classlib BuildingBlocks.Web       src/BuildingBlocks/BuildingBlocks.Web
if ! grep -q 'Microsoft.AspNetCore.App' src/BuildingBlocks/BuildingBlocks.Web/BuildingBlocks.Web.csproj; then
  perl -0pi -e 's#</Project>#  <ItemGroup>\n    <FrameworkReference Include="Microsoft.AspNetCore.App" />\n  </ItemGroup>\n\n</Project>#' \
    src/BuildingBlocks/BuildingBlocks.Web/BuildingBlocks.Web.csproj
fi

dotnet sln "$SLN.sln" add --solution-folder "src/BuildingBlocks" \
  src/BuildingBlocks/Shared.Contracts \
  src/BuildingBlocks/BuildingBlocks.Messaging \
  src/BuildingBlocks/BuildingBlocks.Web

dotnet add src/BuildingBlocks/BuildingBlocks.Messaging reference src/BuildingBlocks/Shared.Contracts

# Sözleşme mimari testleri (Shared.Contracts'ın bağımlılıksız kalmasını ve isim kurallarını korur)
new_project xunit Shared.Contracts.Tests tests/Shared.Contracts.Tests
dotnet sln "$SLN.sln" add --solution-folder "tests" tests/Shared.Contracts.Tests
dotnet add tests/Shared.Contracts.Tests reference src/BuildingBlocks/Shared.Contracts

# Ortak mesajlaşma altyapısının testleri (retry kapsamı vb.)
new_project xunit BuildingBlocks.Messaging.Tests tests/BuildingBlocks.Messaging.Tests
dotnet sln "$SLN.sln" add --solution-folder "tests" tests/BuildingBlocks.Messaging.Tests
dotnet add tests/BuildingBlocks.Messaging.Tests reference src/BuildingBlocks/BuildingBlocks.Messaging

# --- Servisler (Clean Architecture: Domain <- Application <- Infrastructure <- API)
for S in "${SERVICES[@]}"; do
  BASE="src/Services/$S"
  echo ">> Servis: $S"

  new_project classlib "$S.Domain"         "$BASE/$S.Domain"
  new_project classlib "$S.Application"    "$BASE/$S.Application"
  new_project classlib "$S.Infrastructure" "$BASE/$S.Infrastructure"
  new_project webapi   "$S.API"            "$BASE/$S.API" --no-https

  dotnet sln "$SLN.sln" add --solution-folder "src/Services/$S" \
    "$BASE/$S.Domain" "$BASE/$S.Application" "$BASE/$S.Infrastructure" "$BASE/$S.API"

  # Bağımlılık yönü içe doğru; Domain hiçbir şeye bağlı değil.
  dotnet add "$BASE/$S.Application"    reference "$BASE/$S.Domain" src/BuildingBlocks/Shared.Contracts
  dotnet add "$BASE/$S.Infrastructure" reference "$BASE/$S.Application" src/BuildingBlocks/BuildingBlocks.Messaging
  dotnet add "$BASE/$S.API"            reference "$BASE/$S.Application" "$BASE/$S.Infrastructure" src/BuildingBlocks/BuildingBlocks.Web

  # Test projesi
  new_project xunit "$S.UnitTests" "tests/$S.UnitTests"
  dotnet sln "$SLN.sln" add --solution-folder "tests" "tests/$S.UnitTests"
  # Messaging: saga/consumer testlerinde command endpoint eşlemeleri (CommandEndpoints.Map)
  dotnet add "tests/$S.UnitTests" reference "$BASE/$S.Domain" "$BASE/$S.Application" src/BuildingBlocks/BuildingBlocks.Messaging
done

# --- Satır sonu / kodlama normalizasyonu --------------------------------------
# Şablonlar CRLF + UTF-8 BOM yazar; .editorconfig LF ve BOM'suz UTF-8 ister.
find src tests -type f \( -name "*.cs" -o -name "*.csproj" -o -name "*.json" -o -name "*.http" \) \
  -not -path "*/bin/*" -not -path "*/obj/*" -print0 \
  | xargs -0 perl -pi -e 's/\r$//; s/^\xEF\xBB\xBF//'

# --- Central Package Management uyumu ----------------------------------------
# Şablonlar PackageReference'a Version yazar; CPM'de sürüm yalnızca
# Directory.Packages.props'ta olmalı (aksi halde NU1008 hatası).
find src tests -name "*.csproj" -print0 | xargs -0 perl -pi -e \
  's#(<PackageReference Include="[^"]+") Version="[^"]+"#$1#'
# Merkezi props'taki ayarları tekrar eden satırları kaldır
find src tests -name "*.csproj" -print0 | xargs -0 perl -pi -e \
  '$_ = "" if /<(TargetFramework|Nullable|ImplicitUsings)>/'
# Bu satırlar silinince boş kalan <PropertyGroup> bloklarını kaldır
find src tests -name "*.csproj" -print0 | xargs -0 perl -0pi -e \
  's#\n[ \t]*<PropertyGroup>\s*</PropertyGroup>\n#\n#g; s#\n{3,}#\n\n#g'

echo ">> Restore & build"
dotnet restore "$SLN.sln"
dotnet build "$SLN.sln" --no-restore -c Debug

echo ">> Tamamlandı. Altyapıyı başlatmak için:"
echo "   cp .env.example .env && docker compose up -d"
