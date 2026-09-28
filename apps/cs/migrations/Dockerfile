# The Coldframe migration job image: applies every pending migration, then exits 0 (or 1).
# It runs as a Kubernetes Job before the Server rolls; it has no HTTP endpoint.
#
# Build from the repository root:
#   docker buildx build -f apps/cs/migrations/Dockerfile -t coldframe/migrations:dev .
#
# The build stage runs on the build platform and publishes portable, framework-dependent IL, so
# an arm64 image needs no emulation. The final stage only copies files. Configuration comes from
# environment variables only (deploy/images/README.md).

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.103 AS build

ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true

WORKDIR /src

# Restore first, from the project file and lock file alone, so the layer survives source edits.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props BannedSymbols.txt .editorconfig ./
COPY apps/cs/Directory.Build.props apps/cs/
COPY apps/cs/migrations/Coldframe.Migrations.csproj apps/cs/migrations/packages.lock.json apps/cs/migrations/

# Portable restore (no runtime identifier): a RID-specific one would not match the lock file.
RUN dotnet restore apps/cs/migrations/Coldframe.Migrations.csproj --locked-mode

COPY apps/cs/migrations/ apps/cs/migrations/

RUN dotnet publish apps/cs/migrations/Coldframe.Migrations.csproj \
    --configuration Release \
    --no-restore \
    --output /out \
    -p:UseAppHost=false

# The ASP.NET Core runtime, not the plain .NET one: the Escendit service defaults the job uses
# reference the Microsoft.AspNetCore.App shared framework, and the plain runtime image lacks it.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12

WORKDIR /app

COPY --from=build /out/ ./

# The numeric UID of the image's "app" user, so Kubernetes runAsNonRoot can verify it.
USER 1654

ENTRYPOINT ["dotnet", "Coldframe.Migrations.dll"]
