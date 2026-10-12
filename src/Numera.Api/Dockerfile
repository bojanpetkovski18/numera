# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f src/Numera.Api/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first, from the project files only, so the package layer is cached until a project file changes.
COPY Directory.Build.props ./
COPY src/Numera.Api/Numera.Api.csproj src/Numera.Api/
COPY src/Numera.Application/Numera.Application.csproj src/Numera.Application/
COPY src/Numera.Infrastructure/Numera.Infrastructure.csproj src/Numera.Infrastructure/
RUN dotnet restore src/Numera.Api/Numera.Api.csproj
COPY src/ src/
RUN dotnet publish src/Numera.Api/Numera.Api.csproj --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
# The .NET runtime images include a non-root "app" user.
USER $APP_UID
ENTRYPOINT ["dotnet", "Numera.Api.dll"]
