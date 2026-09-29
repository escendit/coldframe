using System.Text;
using Coldframe.Server.Identity.Reconciliation;
using Google.Protobuf;
using Temporalio.Api.Common.V1;
using Temporalio.Converters;

namespace Coldframe.Server.Tests.Identity.Reconciliation;

/// <summary>
/// The workflow arguments as <c>keycloak-temporal-extensions</c> sends them: Jackson camelCase JSON with
/// nulls, read by Temporal's default data converter.
/// </summary>
public sealed class KeycloakEventPayloadTests
{
    [Fact]
    public void ReadsAnAdminEventWithNulls()
    {
        const string json =
            """
            {"id":"6f1c2b0e-8d7a-4c1b-9e2f-3a4b5c6d7e8f","time":1790000000123,"realmId":"coldframe",
             "authDetails":{"realmId":"master","clientId":"security-admin-console","userId":"a1","ipAddress":"10.0.0.2"},
             "resourceType":"ORGANIZATION_MEMBERSHIP","operationType":"CREATE",
             "resourcePath":"orgs/0192f3a4-7c1e-7d2b-9a51-3f7e2c9b1d00/members/5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31",
             "representation":null,"error":null}
            """;

        var adminEvent = Decode<KeycloakAdminEvent>(json);

        Assert.Equal("6f1c2b0e-8d7a-4c1b-9e2f-3a4b5c6d7e8f", adminEvent.Id);
        Assert.Equal(1_790_000_000_123, adminEvent.Time);
        Assert.Equal("coldframe", adminEvent.RealmId);
        Assert.Equal("security-admin-console", adminEvent.AuthDetails?.ClientId);
        Assert.Equal("ORGANIZATION_MEMBERSHIP", adminEvent.ResourceType);
        Assert.Equal("CREATE", adminEvent.OperationType);
        Assert.EndsWith("/members/5b0c7c1e-2a4d-4f1b-8e3a-9d7f6c5b4a31", adminEvent.ResourcePath, StringComparison.Ordinal);
        Assert.Null(adminEvent.Representation);
        Assert.Null(adminEvent.Error);
    }

    [Fact]
    public void ReadsARepresentationAsAString()
    {
        const string json =
            """
            {"id":"e1","time":1,"realmId":"coldframe","authDetails":null,"resourceType":"ORGANIZATION",
             "operationType":"UPDATE","resourcePath":"orgs/o1/o1","representation":"{\"displayName\":\"Garden\"}","error":null}
            """;

        var adminEvent = Decode<KeycloakAdminEvent>(json);

        Assert.Null(adminEvent.AuthDetails);
        Assert.Equal("""{"displayName":"Garden"}""", adminEvent.Representation);
    }

    [Fact]
    public void ReadsAUserEventWithNulls()
    {
        const string json =
            """
            {"id":"u1","time":1790000000123,"type":"LOGIN","realmId":"coldframe","clientId":"coldframe-web",
             "userId":"5b0c7c1e","sessionId":null,"ipAddress":"10.0.0.2","error":null,
             "details":{"auth_method":"openid-connect","redirect_uri":null}}
            """;

        var userEvent = Decode<KeycloakUserEvent>(json);

        Assert.Equal("LOGIN", userEvent.Type);
        Assert.Equal("coldframe-web", userEvent.ClientId);
        Assert.Null(userEvent.SessionId);
        Assert.Equal("openid-connect", userEvent.Details?["auth_method"]);
        Assert.Null(userEvent.Details?["redirect_uri"]);
    }

    private static T Decode<T>(string json)
    {
        var payload = new Payload
        {
            Metadata = { ["encoding"] = ByteString.CopyFromUtf8("json/plain") },
            Data = ByteString.CopyFrom(Encoding.UTF8.GetBytes(json)),
        };

        return (T)DataConverter.Default.PayloadConverter.ToValue(payload, typeof(T))!;
    }
}
