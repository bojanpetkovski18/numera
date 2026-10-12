# syntax=docker/dockerfile:1
# The production image: the ASP.NET Core API, which also serves the built Angular app from wwwroot (one origin,
# one service). Used by docker-compose.yml, the CI end-to-end test and the Render deployment (render.yaml).
# Build from the repository root: docker build -t numera .

# 1. Angular app
FROM node:24-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# 2. API. Restore from the project files first so the package layer stays cached until a project file changes.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Numera.Api/Numera.Api.csproj src/Numera.Api/
COPY src/Numera.Application/Numera.Application.csproj src/Numera.Application/
COPY src/Numera.Infrastructure/Numera.Infrastructure.csproj src/Numera.Infrastructure/
RUN dotnet restore src/Numera.Api/Numera.Api.csproj
COPY src/ src/
RUN dotnet publish src/Numera.Api/Numera.Api.csproj --configuration Release --no-restore --output /app /p:UseAppHost=false

# 3. Runtime: ASP.NET Core only, running as the image's non-root "app" user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=api /app .
COPY --from=web /web/dist/web/browser ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Numera.Api.dll"]
