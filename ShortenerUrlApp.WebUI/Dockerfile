FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files
COPY ["ShortenerUrlApp.WebUI/ShortenerUrlApp.WebUI.csproj", "ShortenerUrlApp.WebUI/"]
COPY ["ShortenerUrlApp.Shared/ShortenerUrlApp.Shared.csproj", "ShortenerUrlApp.Shared/"]

# Restore dependencies
RUN dotnet restore "ShortenerUrlApp.WebUI/ShortenerUrlApp.WebUI.csproj"

# Copy source code
COPY ShortenerUrlApp.WebUI/ ShortenerUrlApp.WebUI/
COPY ShortenerUrlApp.Shared/ ShortenerUrlApp.Shared/

# Build
WORKDIR "/src/ShortenerUrlApp.WebUI"
RUN dotnet build "ShortenerUrlApp.WebUI.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "ShortenerUrlApp.WebUI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html
COPY --from=publish /app/publish/wwwroot .
COPY ShortenerUrlApp.WebUI/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
CMD ["nginx", "-g", "daemon off;"]
