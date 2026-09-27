// The local development stack. The integration tests start this same AppHost (AD-15, AD-24).
// It generates no deployment artifacts. Every image is pinned to an explicit version.

const string TemporalNamespace = "coldframe";

var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL holds every durable fact (AD-5). Keycloak gets its own database (AD-15).
var postgres = builder
    .AddPostgres("postgres")
    .WithImageTag("18.6");

var serverDatabase = postgres.AddDatabase("coldframe");
var keycloakDatabase = postgres.AddDatabase("keycloak-database", "keycloak");

// NATS JetStream backs Orleans streams only (AD-5).
var nats = builder
    .AddNats("nats")
    .WithImageTag("2.15.0")
    .WithJetStream();

// Temporal carries only the Keycloak event pipeline (AD-3, AD-5).
// No public image exists for Temporal Server 1.31.3, so the local stack runs the Temporal CLI
// development server: one container, in-memory persistence, no schema setup.
// CLI 1.8.3 bundles Temporal Server 1.31.2.
var temporal = builder
    .AddContainer("temporal", "temporalio/temporal", "1.8.3")
    .WithImageRegistry("docker.io")
    .WithArgs("server", "start-dev", "--ip", "0.0.0.0", "--namespace", TemporalNamespace)
    .WithEndpoint(name: "grpc", scheme: "tcp", targetPort: 7233)
    .WithHttpEndpoint(name: "ui", targetPort: 8233)
    .WithHttpHealthCheck($"/api/v1/namespaces/{TemporalNamespace}", endpointName: "ui");

// Local-only credentials. The password is generated on each start and never written to the repository.
var keycloakAdminUsername = builder.AddParameter("keycloak-admin-username", "admin");
var keycloakAdminPassword = builder.AddParameter(
    "keycloak-admin-password",
    new GenerateParameterDefault { MinLength = 24, Special = false },
    secret: true);

// Phase Two Keycloak with keycloak-temporal-extensions, built from ../keycloak/Dockerfile.
var postgresEndpoint = postgres.GetEndpoint("tcp");
var temporalEndpoint = temporal.GetEndpoint("grpc");

var keycloak = builder
    .AddDockerfile("keycloak", "../keycloak")
    .WithArgs("start", "--optimized")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", keycloakAdminUsername)
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakAdminPassword)
    .WithEnvironment("KC_DB_URL_HOST", postgresEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("KC_DB_URL_PORT", postgresEndpoint.Property(EndpointProperty.Port))
    .WithEnvironment("KC_DB_URL_DATABASE", keycloakDatabase.Resource.DatabaseName)
    .WithEnvironment("KC_DB_USERNAME", postgres.Resource.UserNameReference)
    .WithEnvironment("KC_DB_PASSWORD", postgres.Resource.PasswordParameter)
    .WithEnvironment("KC_HTTP_ENABLED", "true")
    .WithEnvironment("KC_HOSTNAME_STRICT", "false")
    .WithEnvironment("KC_SPI_EVENTS_LISTENER__TEMPORAL__TARGET_HOST", temporalEndpoint.Property(EndpointProperty.HostAndPort))
    .WithEnvironment("KC_SPI_EVENTS_LISTENER__TEMPORAL__NAMESPACE", TemporalNamespace)
    .WithHttpEndpoint(name: "http", targetPort: 8080)
    .WithHttpEndpoint(name: "management", targetPort: 9000)
    .WithHttpHealthCheck("/health/ready", endpointName: "management")
    .WaitFor(keycloakDatabase)
    .WaitFor(temporal);

// The Server: Orleans silo and Edge API in one ASP.NET Core host.
// The silo ports are allocated per run, so the stack and the tests can run side by side.
builder
    .AddProject<Projects.Coldframe_Server>("server")
    .WithReference(serverDatabase)
    .WithReference(nats)
    .WithEndpoint(name: "silo", scheme: "tcp", env: "Orleans__Endpoints__SiloPort", isProxied: false)
    .WithEndpoint(name: "gateway", scheme: "tcp", env: "Orleans__Endpoints__GatewayPort", isProxied: false)
    .WithHttpHealthCheck("/.well-known/healthz")
    .WaitFor(serverDatabase)
    .WaitFor(nats)
    .WaitFor(temporal)
    .WaitFor(keycloak);

await builder.Build().RunAsync().ConfigureAwait(false);
