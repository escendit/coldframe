# The Coldframe Server image: Orleans silo, Edge API and SignalR in one ASP.NET Core host.
#
# Build from the repository root:
#   docker buildx build -f apps/cs/server/Dockerfile -t coldframe/server:dev .
#
# The build stage runs on the build platform and publishes portable, framework-dependent IL, so
# an arm64 image needs no emulation. The final stage only copies files. Configuration comes from
# environment variables only (deploy/images/README.md).

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.103 AS build

ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true

WORKDIR /src

# Restore first, from the project files and lock files alone, so the layer survives source edits.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props BannedSymbols.txt .editorconfig ./
COPY apps/cs/Directory.Build.props apps/cs/
COPY packages/cs/Directory.Build.props packages/cs/
COPY apps/cs/server/Coldframe.Server.csproj apps/cs/server/packages.lock.json apps/cs/server/
COPY packages/cs/contracts/Coldframe.Contracts.csproj packages/cs/contracts/packages.lock.json packages/cs/contracts/

# Portable restore (no runtime identifier): a RID-specific one would not match the lock files.
RUN dotnet restore apps/cs/server/Coldframe.Server.csproj --locked-mode

COPY apps/cs/server/ apps/cs/server/
COPY packages/cs/contracts/ packages/cs/contracts/

RUN dotnet publish apps/cs/server/Coldframe.Server.csproj \
    --configuration Release \
    --no-restore \
    --output /out \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12

WORKDIR /app

COPY --from=build /out/ ./

# HTTP (Edge API and health), Orleans silo-to-silo, Orleans gateway. The silo ports can be moved
# with Orleans__Endpoints__SiloPort and Orleans__Endpoints__GatewayPort.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080 11111 30000

# The numeric UID of the image's "app" user, so Kubernetes runAsNonRoot can verify it.
USER 1654

ENTRYPOINT ["dotnet", "Coldframe.Server.dll"]
