using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Coldframe.Server.Edge;

/// <summary>
/// How the Edge API validates Keycloak access tokens (configuration section <see cref="SectionName"/>).
/// </summary>
public sealed class EdgeIdentityOptions
{
    /// <summary>
    /// The configuration section.
    /// </summary>
    public const string SectionName = "Identity";

    /// <summary>
    /// The token issuer, such as <c>https://id.example.org/realms/coldframe</c>.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// The audience every access token must carry.
    /// </summary>
    public string Audience { get; set; } = "coldframe-server";

    /// <summary>
    /// Whether the discovery document must be fetched over HTTPS. Only the local stack turns it off.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}

/// <summary>
/// Registers authentication, the shared authorization policy, Problem Details and the Edge API's JSON
/// conventions (camelCase, enums as strings, absent optional fields omitted).
/// </summary>
public static class EdgeAuthentication
{
    /// <summary>
    /// The claim that carries the User ID. Inbound claims are not mapped, so it stays <c>sub</c>.
    /// </summary>
    public const string UserIdClaim = "sub";

    /// <summary>
    /// The paths that stay anonymous: the health endpoints.
    /// </summary>
    public static readonly PathString HealthPath = new("/.well-known/healthz");

    /// <summary>
    /// Adds the Edge API's services.
    /// </summary>
    public static WebApplicationBuilder AddEdgeApi(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var settings = builder.Configuration.GetSection(EdgeIdentityOptions.SectionName).Get<EdgeIdentityOptions>()
            ?? new EdgeIdentityOptions();

        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = settings.Authority;
                options.Audience = settings.Audience;
                options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = UserIdClaim;
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidateLifetime = true;
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (string.IsNullOrEmpty(context.Principal?.FindFirst(UserIdClaim)?.Value))
                        {
                            context.Fail("The access token carries no 'sub'.");
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();

                        if (context.Response.HasStarted)
                        {
                            return;
                        }

                        context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
                        await EdgeProblems.WriteAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            EdgeProblems.Unauthorized,
                            "Sign in again.",
                            "The request carries no valid access token.").ConfigureAwait(false);
                    },
                };
            });

        builder.Services.AddOptions<EdgeIdentityOptions>()
            .Bind(builder.Configuration.GetSection(EdgeIdentityOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.Authority, UriKind.Absolute, out _), "Identity:Authority must be an absolute URL.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Identity:Audience is required.")
            .ValidateOnStart();

        builder.Services.AddSingleton<IAuthorizationHandler, SiteAccessHandler>();
        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, EdgeAuthorizationResultHandler>();
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(EdgeAccessRuleExtensions.AuthenticatedCallerPolicy, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(EdgeAccessRuleExtensions.SiteRolePolicy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new SiteAccessRequirement()))
            // Everything else needs an authenticated caller too, except the health endpoints.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAssertion(context =>
                    context.User.Identity?.IsAuthenticated == true
                    || (context.Resource is HttpContext http && http.Request.Path.StartsWithSegments(HealthPath)))
                .Build());

        return builder;
    }

    /// <summary>
    /// Turns authentication and authorization on for every request.
    /// </summary>
    public static WebApplication UseEdgeApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}
