FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
# Match the EXPOSE port and docker-compose health check (plain HTTP inside the container).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
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
RUN dotnet publish "ShortenerUrlApp.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ShortenerUrlApp.WebApi.dll"]
