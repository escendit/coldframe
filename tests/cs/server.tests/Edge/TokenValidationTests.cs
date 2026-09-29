using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// The token rules <c>AddEdgeApi</c> configures, checked on locally signed tokens: only the signing key and
/// the issuer (which the running Server reads from Keycloak's discovery document) are swapped in.
/// </summary>
public sealed class TokenValidationTests
{
    private const string Issuer = EdgeTestHost.Authority;
    private const string Audience = "coldframe-server";

    private static readonly SymmetricSecurityKey SigningKey = new(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());

    [Fact]
    public void TheConfiguredRulesRequireTheServerAudienceIssuerAndLifetime()
    {
        var parameters = ConfiguredParameters();

        Assert.True(parameters.ValidateAudience);
        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateLifetime);
        Assert.Equal(Audience, parameters.ValidAudience);
        Assert.Equal("sub", parameters.NameClaimType);
    }

    [Fact]
    public async Task AValidTokenIsAccepted()
    {
        var result = await ValidateAsync(CreateToken());

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal("user-1", result.ClaimsIdentity.Name);
    }

    [Fact]
    public async Task AnExpiredTokenIsRejected()
    {
        // Expired well beyond the clock skew the bearer handler allows.
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        var parameters = ConfiguredParameters();
        var expires = now - parameters.ClockSkew - TimeSpan.FromMinutes(1);

        var result = await ValidateAsync(CreateToken(notBefore: expires - TimeSpan.FromMinutes(5), expires: expires));

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenExpiredException>(result.Exception);
    }

    [Fact]
    public async Task ATokenWithoutTheServerAudienceIsRejected()
    {
        var result = await ValidateAsync(CreateToken(audience: "account"));

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidAudienceException>(result.Exception);
    }

    [Fact]
    public async Task ATokenFromAnotherIssuerIsRejected()
    {
        var result = await ValidateAsync(CreateToken(issuer: "https://id.test.invalid/realms/master"));

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidIssuerException>(result.Exception);
    }

    private static TokenValidationParameters ConfiguredParameters()
    {
        using var app = EdgeTestHost.Build();
        var options = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        return options.TokenValidationParameters.Clone();
    }

    private static Task<TokenValidationResult> ValidateAsync(string token)
    {
        var parameters = ConfiguredParameters();
        parameters.IssuerSigningKey = SigningKey;
        parameters.ValidIssuer = Issuer;

        return new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
    }

    private static string CreateToken(
        string issuer = Issuer,
        string audience = Audience,
        DateTime? notBefore = null,
        DateTime? expires = null)
    {
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", "user-1")]),
            IssuedAt = notBefore ?? now,
            NotBefore = notBefore ?? now,
            Expires = expires ?? now + TimeSpan.FromMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }
}
