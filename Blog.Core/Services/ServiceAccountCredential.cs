using System.Text.Json;

namespace Blog.Core.Services;

/// <summary>
/// The parts of a Google service-account key file the platform needs, parsed and validated. The
/// operator pastes the JSON Google hands them; the platform keeps only what it can use. Nothing here
/// touches the network.
/// </summary>
public sealed record ServiceAccountCredential(string ClientEmail, string PrivateKeyPem, string? ProjectId)
{
    /// <summary>
    /// Parses a service-account JSON document. Returns null with a reason when the document is not a
    /// service-account key (an OAuth client file, an API key, or plain garbage look different).
    /// </summary>
    public static ServiceAccountCredential? TryParse(string? json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) { error = "No credential supplied."; return null; }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = "Not a JSON object."; return null; }

            var type = Get(root, "type");
            if (!string.Equals(type, "service_account", StringComparison.Ordinal))
            {
                error = type is null
                    ? "This is not a service-account key file (no \"type\" field). Create one under IAM → Service Accounts → Keys."
                    : $"This is a \"{type}\" credential, not a service account key.";
                return null;
            }

            var email = Get(root, "client_email");
            var key = Get(root, "private_key");
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) { error = "client_email is missing."; return null; }
            if (string.IsNullOrWhiteSpace(key) || !key.Contains("PRIVATE KEY")) { error = "private_key is missing or not a PEM block."; return null; }

            return new ServiceAccountCredential(email.Trim(), key, Get(root, "project_id"));
        }
        catch (JsonException)
        {
            error = "The credential is not valid JSON.";
            return null;
        }
    }

    private static string? Get(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
