FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
# Match the EXPOSE port and docker-compose health check (plain HTTP inside the container).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files
COPY ["ShortenerUrl.WebApi/ShortenerUrlApp.WebApi.csproj", "ShortenerUrl.WebApi/"]
COPY ["ShortenerUrlApp.Shared/ShortenerUrlApp.Shared.csproj", "ShortenerUrlApp.Shared/"]
COPY ["ShortenerUrlApp.Tests/ShortenerUrlApp.Tests.csproj", "ShortenerUrlApp.Tests/"]

# Restore dependencies
RUN dotnet restore "ShortenerUrl.WebApi/ShortenerUrlApp.WebApi.csproj"

# Copy source code
COPY ShortenerUrl.WebApi/ ShortenerUrl.WebApi/
COPY ShortenerUrlApp.Shared/ ShortenerUrlApp.Shared/
COPY ShortenerUrlApp.Tests/ ShortenerUrlApp.Tests/

# Build
WORKDIR "/src/ShortenerUrl.WebApi"
RUN dotnet build "ShortenerUrlApp.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Run tests
WORKDIR "/src/ShortenerUrlApp.Tests"
RUN dotnet test "ShortenerUrlApp.Tests.csproj" -c $BUILD_CONFIGURATION --no-build

# Publish
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR "/src/ShortenerUrl.WebApi"
RUN dotnet publish "ShortenerUrlApp.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# GeoLite2-City database (offline GeoIP). Downloading it from MaxMind needs a free license
# key (build arg MAXMIND_LICENSE_KEY, https://www.maxmind.com). Without a key the community
# P3TERX mirror is used instead. If both downloads fail the stage still succeeds with an empty
# file and the API runs with GeoIP enrichment disabled (Country/City stay null).
FROM alpine:3 AS geolite
ARG MAXMIND_LICENSE_KEY=
RUN if [ -n "$MAXMIND_LICENSE_KEY" ]; then \
        wget -qO /tmp/geolite.tar.gz "https://download.maxmind.com/app/geoip_download?edition_id=GeoLite2-City&license_key=${MAXMIND_LICENSE_KEY}&suffix=tar.gz" && \
        mkdir -p /tmp/geolite && \
        tar -xzf /tmp/geolite.tar.gz -C /tmp/geolite && \
        cp /tmp/geolite/GeoLite2-City_*/GeoLite2-City.mmdb /tmp/GeoLite2-City.mmdb || true; \
    fi; \
    if [ ! -s /tmp/GeoLite2-City.mmdb ]; then \
        wget -qO /tmp/GeoLite2-City.mmdb "https://github.com/P3TERX/GeoLite.mmdb/raw/download/GeoLite2-City.mmdb" || true; \
    fi; \
    [ -s /tmp/GeoLite2-City.mmdb ] || touch /tmp/GeoLite2-City.mmdb

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=geolite /tmp/GeoLite2-City.mmdb /app/Data/GeoLite2-City.mmdb
ENTRYPOINT ["dotnet", "ShortenerUrlApp.WebApi.dll"]
