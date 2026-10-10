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

// The secret of the coldframe-web client. Keycloak substitutes it into the imported realm file.
var coldframeWebClientSecret = builder.AddParameter(
    "coldframe-web-client-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true);

// The secret of the coldframe-server service account, which the Server uses to manage Phase Two
// Organizations. Keycloak substitutes it into the imported realm file; the Server reads it from its environment.
var coldframeServerClientSecret = builder.AddParameter(
    "coldframe-server-client-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true);

// Device enrolment (AD-12). The Server's X25519 enrolment private key is SHA-256 of a generated seed, handed
// to the Server as PKCS#8 PEM, as the Secret coldframe-enrolment-key holds it in a deployment. The Device
// key-encryption key is a separate generated secret, as the Secret coldframe-device-kek. Nothing is
// written to the repository.
var enrolmentKeySeed = builder.AddParameter(
    "enrolment-key-seed",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true);
var deviceKek = builder.AddParameter(
    "device-kek",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true);

// Phase Two Keycloak with keycloak-temporal-extensions, built from ../keycloak/Dockerfile.
var postgresEndpoint = postgres.GetEndpoint("tcp");
var temporalEndpoint = temporal.GetEndpoint("grpc");

var keycloak = builder
    .AddDockerfile("keycloak", "../keycloak")
    .WithArgs("start", "--optimized", "--import-realm")
    // The coldframe realm and its coldframe-web client. A realm that already exists is left as it is.
    // The files are copied into the container rather than bind-mounted, so SELinux labels on the
    // host directory cannot hide them from Keycloak.
    .WithContainerFiles("/opt/keycloak/data/import", "../keycloak/realms")
    .WithEnvironment("COLDFRAME_WEB_CLIENT_SECRET", coldframeWebClientSecret)
    .WithEnvironment("COLDFRAME_SERVER_CLIENT_SECRET", coldframeServerClientSecret)
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

// The migration job applies the one forward-only migration set, then exits (AD-22).
// The Server starts only after it has finished successfully; the Server itself never runs DDL.
var migrations = builder
    .AddProject<Projects.Coldframe_Migrations>("migrations")
    .WithReference(serverDatabase)
    .WaitFor(serverDatabase);

// The Server: Orleans silo and Edge API in one ASP.NET Core host.
// The silo ports are allocated per run, so the stack and the tests can run side by side.
// It validates access tokens of the coldframe realm and manages Organizations with its own service
// account; plain-HTTP metadata is allowed only because this stack runs locally. Its Temporal workers
// consume the Keycloak event pipeline of the coldframe realm, whose ID the realm file fixes (AD-3, AD-5).
var keycloakHttp = keycloak.GetEndpoint("http");

var server = builder
    .AddProject<Projects.Coldframe_Server>("server")
    .WithReference(serverDatabase)
    .WithReference(nats)
    .WithEnvironment("Identity__Authority", ReferenceExpression.Create($"{keycloakHttp}/realms/coldframe"))
    .WithEnvironment("Identity__Audience", "coldframe-server")
    .WithEnvironment("Identity__RequireHttpsMetadata", "false")
    .WithEnvironment("Keycloak__BaseUrl", keycloakHttp)
    .WithEnvironment("Keycloak__Realm", "coldframe")
    .WithEnvironment("Keycloak__ClientId", "coldframe-server")
    .WithEnvironment("Keycloak__ClientSecret", coldframeServerClientSecret)
    .WithEnvironment("KeycloakEvents__TargetHost", temporalEndpoint.Property(EndpointProperty.HostAndPort))
    .WithEnvironment("KeycloakEvents__Namespace", TemporalNamespace)
    .WithEnvironment("KeycloakEvents__RealmId", "coldframe")
    .WithEnvironment("Enrolment__DeviceKeyEncryptionKey", deviceKek)
    .WithEnvironment(async context =>
    {
        var seed = await enrolmentKeySeed.Resource.GetValueAsync(context.CancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The parameter 'enrolment-key-seed' has no value.");
        context.EnvironmentVariables["Enrolment__PrivateKeyPem"] = EnrolmentKeyPem(seed);
    })
    .WithEndpoint(name: "silo", scheme: "tcp", env: "Orleans__Endpoints__SiloPort", isProxied: false)
    .WithEndpoint(name: "gateway", scheme: "tcp", env: "Orleans__Endpoints__GatewayPort", isProxied: false)
    .WithHttpHealthCheck("/.well-known/healthz")
    .WaitForCompletion(migrations)
    .WaitFor(nats)
    .WaitFor(temporal)
    .WaitFor(keycloak);

// Push notifications (Story 6.5) are optional, as the Secret coldframe-push is in a deployment: the Server
// runs without them and logs once that a provider has no credentials. To send real pushes from the local
// stack, give the AppHost your own credentials as environment variables (Push__Apns__KeyId,
// Push__Apns__TeamId, Push__Apns__PrivateKeyPem, Push__Apns__Topic, Push__Fcm__ServiceAccountJson). Only what
// is set is handed on; nothing is generated and nothing is written to the repository.
string[] pushSettings =
[
    "Push:Apns:KeyId",
    "Push:Apns:TeamId",
    "Push:Apns:PrivateKeyPem",
    "Push:Apns:Topic",
    "Push:Apns:ProductionBaseUrl",
    "Push:Apns:SandboxBaseUrl",
    "Push:Fcm:ServiceAccountJson",
    "Push:Fcm:BaseUrl",
    "Push:Fcm:TokenUrl",
];

foreach (var setting in pushSettings)
{
    if (builder.Configuration[setting] is { Length: > 0 } value)
    {
        server.WithEnvironment(setting.Replace(":", "__", StringComparison.Ordinal), value);
    }
}

await builder.Build().RunAsync().ConfigureAwait(false);

// PKCS#8 PEM of the X25519 private key SHA-256(seed), as "openssl genpkey -algorithm X25519" writes it.
static string EnrolmentKeyPem(string seed)
{
    var der = new byte[48];
    Convert.FromHexString("302e020100300506032b656e04220420").CopyTo(der, 0);
    System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed), der.AsSpan(16));
    return new string(System.Security.Cryptography.PemEncoding.Write("PRIVATE KEY", der));
}
