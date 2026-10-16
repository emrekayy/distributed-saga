# syntax=docker/dockerfile:1
# Build context: repo kökü  →  docker build -f src/Services/Inventory/Inventory.API/Dockerfile .
# Seçimler ve gerekçeler: docs/decisions.md (ADR-022).

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore katmanı: yalnızca props/csproj'lar kopyalanır; kaynak değişince paketler yeniden indirilmez.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/BuildingBlocks/Shared.Contracts/Shared.Contracts.csproj src/BuildingBlocks/Shared.Contracts/
COPY src/BuildingBlocks/BuildingBlocks.Messaging/BuildingBlocks.Messaging.csproj src/BuildingBlocks/BuildingBlocks.Messaging/
COPY src/BuildingBlocks/BuildingBlocks.Web/BuildingBlocks.Web.csproj src/BuildingBlocks/BuildingBlocks.Web/
COPY src/Services/Inventory/Inventory.Domain/Inventory.Domain.csproj src/Services/Inventory/Inventory.Domain/
COPY src/Services/Inventory/Inventory.Application/Inventory.Application.csproj src/Services/Inventory/Inventory.Application/
COPY src/Services/Inventory/Inventory.Infrastructure/Inventory.Infrastructure.csproj src/Services/Inventory/Inventory.Infrastructure/
COPY src/Services/Inventory/Inventory.API/Inventory.API.csproj src/Services/Inventory/Inventory.API/
RUN dotnet restore src/Services/Inventory/Inventory.API/Inventory.API.csproj

COPY src src
RUN dotnet publish src/Services/Inventory/Inventory.API/Inventory.API.csproj -c Release --no-restore -o /app /p:UseAppHost=false

# Chiseled: shell, paket yöneticisi ve curl yok; healthcheck için "--health-probe" argümanı (BuildingBlocks.Web).
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled-extra AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Inventory.API.dll"]
