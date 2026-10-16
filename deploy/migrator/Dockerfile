# syntax=docker/dockerfile:1
# Tek seferlik migration job'ı (init container kalıbı): üç servisin EF migration bundle'ını içerir.
# Build context: repo kökü  →  docker build -f deploy/migrator/Dockerfile .
# Bundle'lar self-contained'dir; runtime'da SDK, kaynak kod ya da dotnet-ef gerekmez (ADR-016/022).

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN dotnet tool restore \
 && case "$TARGETARCH" in amd64) RID=linux-x64 ;; arm64) RID=linux-arm64 ;; *) echo "unsupported arch $TARGETARCH" >&2; exit 1 ;; esac \
 && for svc in Ordering Inventory Payments; do \
      dotnet ef migrations bundle --self-contained -r "$RID" --force \
        -p "src/Services/$svc/$svc.Infrastructure" -s "src/Services/$svc/$svc.API" \
        -o "/out/efbundle-$svc" || exit 1; \
    done

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-noble AS final
WORKDIR /app
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
COPY --from=build --chmod=755 /out/ ./
COPY --chmod=755 deploy/migrator/migrate.sh ./migrate.sh
USER $APP_UID
ENTRYPOINT ["./migrate.sh"]
