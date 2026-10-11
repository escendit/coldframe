using System.Text.Json;
using System.Text.Json.Serialization;
using Coldframe.Server.Journal;
using Coldframe.Server.Notifications.Push;

namespace Coldframe.Server.Live;

/// <summary>
/// <c>notification.delivered</c> (Story 6.6): what one notification says to an open web app and where a click
/// on it leads. The contract is the schema <c>NotificationDelivered</c> of <c>packages/asyncapi</c>; title and
/// body are the push text.
/// </summary>
/// <param name="Kind">The contract's kind: <c>alert</c>, <c>reminder</c> or <c>summary</c>.</param>
/// <param name="SiteId">The Site the notification is about.</param>
/// <param name="SiteName">The Site's name; empty when it was not found.</param>
/// <param name="LotId">The Lot of the Alert; absent in a summary.</param>
/// <param name="AlertId">The Alert; absent in a summary.</param>
/// <param name="CollapseId">The stable identity of the send, which the browser uses as the notification tag.</param>
/// <param name="Title">The condition.</param>
/// <param name="Body">Value and context; empty when the Server found neither.</param>
public sealed record NotificationDeliveredMessage(
    string Kind,
    string SiteId,
    string SiteName,
    string? LotId,
    Guid? AlertId,
    string CollapseId,
    string Title,
    string Body)
{
    /// <summary>
    /// The message for a push content: the same words a phone shows.
    /// </summary>
    public static NotificationDeliveredMessage From(PushContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new NotificationDeliveredMessage(
            content.Kind,
            content.SiteId,
            content.SiteName ?? string.Empty,
            content.LotId,
            content.AlertId,
            content.CollapseId,
            content.Title,
            content.Body ?? string.Empty);
    }
}

/// <summary>
/// <c>readmodel.changed</c> (Story 6.6): a hint that a read model changed. It has these three fields and
/// nothing derived from domain data; the contract is the schema <c>ReadModelChanged</c> of
/// <c>packages/asyncapi</c>.
/// </summary>
/// <param name="Resource">One of <see cref="ReadModelResources"/>.</param>
/// <param name="Id">The ID of what changed.</param>
/// <param name="Version">A positive number that never decreases for the same resource and ID.</param>
public sealed record ReadModelChangedMessage(string Resource, string Id, long Version);

/// <summary>
/// The names and the JSON conventions of the hub's two messages.
/// </summary>
public static class LiveMessages
{
    /// <summary>
    /// The hub's path on the Server. The web app's backend-for-frontend serves the same path on its own origin.
    /// </summary>
    public const string HubPath = "/live";

    /// <summary>
    /// The invocation target of <see cref="NotificationDeliveredMessage"/>.
    /// </summary>
    public const string NotificationDelivered = "notification.delivered";

    /// <summary>
    /// The invocation target of <see cref="ReadModelChangedMessage"/>.
    /// </summary>
    public const string ReadModelChanged = "readmodel.changed";

    /// <summary>
    /// Applies the conventions the contract is written in: camelCase names, and an absent optional field is
    /// left out, never <c>null</c>.
    /// </summary>
    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

        return options;
    }
}
