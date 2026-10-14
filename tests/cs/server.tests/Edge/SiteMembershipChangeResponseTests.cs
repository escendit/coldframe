using System.Reflection;
using System.Text.Json;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Edge;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// How <c>PUT</c> and <c>DELETE /sites/{siteId}/members/{userId}</c> answer each outcome of the Site grain, and how
/// the Edge tells the person's User grain afterwards (Story 9.2), written through the Server's own JSON and Problem
/// Details configuration.
/// </summary>
public sealed class SiteMembershipChangeResponseTests
{
    [Theory]
    [InlineData(SiteMembershipChangeOutcome.Changed)]
    [InlineData(SiteMembershipChangeOutcome.Unchanged)]
    public async Task AChangedOrUnchangedMembershipAnswers204WithNoBody(SiteMembershipChangeOutcome outcome)
    {
        var (context, body) = await ExecuteAsync(EdgeApi.ToHttpResult(new SiteMembershipChangeResult(outcome, SiteRole.Member)));

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.Empty(body);
    }

    [Theory]
    [InlineData(SiteMembershipChangeOutcome.LastOwner, StatusCodes.Status409Conflict, EdgeProblems.LastOwner, "A Site always keeps at least one Owner.")]
    [InlineData(SiteMembershipChangeOutcome.MemberNotFound, StatusCodes.Status404NotFound, EdgeProblems.MemberNotFound, "That person is not a member of this Site.")]
    [InlineData(SiteMembershipChangeOutcome.IdentityProviderUnavailable, StatusCodes.Status503ServiceUnavailable, EdgeProblems.IdentityProviderUnavailable, "The sign-in service is unavailable.")]
    [InlineData(SiteMembershipChangeOutcome.NotFound, StatusCodes.Status404NotFound, EdgeProblems.SiteNotFound, "The Site does not exist.")]
    public async Task RefusalsAnswerProblemDetailsOfTheirType(SiteMembershipChangeOutcome outcome, int status, string type, string title)
    {
        var (context, text) = await ExecuteAsync(EdgeApi.ToHttpResult(new SiteMembershipChangeResult(outcome)));
        using var body = JsonDocument.Parse(text);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith(EdgeProblems.ContentType, context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal(type, body.RootElement.GetProperty("type").GetString());
        Assert.Equal(title, body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task TheUnavailableAnswerSaysTheChangeWasNotSaved()
    {
        var (_, text) = await ExecuteAsync(EdgeApi.ToHttpResult(new SiteMembershipChangeResult(SiteMembershipChangeOutcome.IdentityProviderUnavailable)));
        using var body = JsonDocument.Parse(text);

        Assert.Equal("The change was not saved. Try again.", body.RootElement.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(SiteRole.Administrator)]
    [InlineData(null)]
    public async Task AChangedMembershipIsHandedToThePersonsUserGrain(SiteRole? role)
    {
        var grains = new UserGrains(fail: false);

        var result = await EdgeApi.MembershipChangedAsync(
            grains,
            "site-a",
            "ana",
            new SiteMembershipChangeResult(SiteMembershipChangeOutcome.Changed, role),
            new ListLogger());

        Assert.Equal(("ana", "site-a", role), Assert.Single(grains.Synced));
        Assert.Equal(StatusCodes.Status204NoContent, (await ExecuteAsync(result)).Context.Response.StatusCode);
    }

    [Theory]
    [InlineData(SiteMembershipChangeOutcome.Unchanged)]
    [InlineData(SiteMembershipChangeOutcome.LastOwner)]
    [InlineData(SiteMembershipChangeOutcome.MemberNotFound)]
    [InlineData(SiteMembershipChangeOutcome.IdentityProviderUnavailable)]
    [InlineData(SiteMembershipChangeOutcome.NotFound)]
    public async Task NothingIsHandedToTheUserGrainWhenNothingChanged(SiteMembershipChangeOutcome outcome)
    {
        var grains = new UserGrains(fail: false);

        await EdgeApi.MembershipChangedAsync(grains, "site-a", "ana", new SiteMembershipChangeResult(outcome, SiteRole.Member), new ListLogger());

        Assert.Empty(grains.Synced);
    }

    [Fact]
    public async Task AUserGrainThatFailsIsLoggedAndTheChangeStillAnswers204()
    {
        var grains = new UserGrains(fail: true);
        var logger = new ListLogger();

        var result = await EdgeApi.MembershipChangedAsync(
            grains,
            "site-a",
            "ana",
            new SiteMembershipChangeResult(SiteMembershipChangeOutcome.Changed),
            logger);

        Assert.Equal(StatusCodes.Status204NoContent, (await ExecuteAsync(result)).Context.Response.StatusCode);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("MembershipSyncFailed", entry.EventId.Name);
    }

    private static async Task<(DefaultHttpContext Context, string Body)> ExecuteAsync(IResult result)
    {
        await using var app = EdgeTestHost.Build();
        using var stream = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Response.Body = stream;

        await result.ExecuteAsync(context);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return (context, await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, EventId EventId)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId));
    }

    // Only User grains by string key, and of them only SyncSiteMembership.
    private sealed class UserGrains(bool fail) : IGrainFactory
    {
        public List<(string UserId, string SiteId, SiteRole? Role)> Synced { get; } = [];

        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithStringKey
        {
            if (typeof(TGrainInterface) != typeof(IUserGrain))
            {
                throw new NotSupportedException();
            }

            var grain = DispatchProxy.Create<IUserGrain, UserGrainProxy>();
            ((UserGrainProxy)(object)grain).Sync = (siteId, role) =>
            {
                if (fail)
                {
                    throw new InvalidOperationException("The User grain did not answer.");
                }

                Synced.Add((primaryKey, siteId, role));
            };
            return (TGrainInterface)grain;
        }

        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithGuidKey => throw new NotSupportedException();

        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithIntegerKey => throw new NotSupportedException();

        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithGuidCompoundKey => throw new NotSupportedException();

        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithIntegerCompoundKey => throw new NotSupportedException();

        public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj)
            where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();

        public void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj)
            where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();

        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey) => throw new NotSupportedException();

        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey) => throw new NotSupportedException();

        public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey) => throw new NotSupportedException();

        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string keyExtension) => throw new NotSupportedException();

        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string keyExtension) => throw new NotSupportedException();

        public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId)
            where TGrainInterface : IAddressable => throw new NotSupportedException();

        public IAddressable GetGrain(GrainId grainId) => throw new NotSupportedException();

        public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType) => throw new NotSupportedException();

        public IAddressable GetGrain(Type interfaceType, IdSpan grainKey) => throw new NotSupportedException();

        public IAddressable GetGrain(Type interfaceType, IdSpan grainKey, string grainClassNamePrefix) => throw new NotSupportedException();
    }

#pragma warning disable CA1852 // DispatchProxy derives from it at run time.
    internal class UserGrainProxy : DispatchProxy
#pragma warning restore CA1852
    {
        public Action<string, SiteRole?>? Sync { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IUserGrain.SyncSiteMembership) || args is null)
            {
                throw new NotSupportedException(targetMethod?.Name);
            }

            try
            {
                Sync!((string)args[0]!, (SiteRole?)args[1]);
                return Task.CompletedTask;
            }
#pragma warning disable CA1031 // The grain's failure is the task's, as a grain call fails.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                return Task.FromException(exception);
            }
        }
    }
}
