using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Membership.Functions;

public sealed record ValidationError(string Field, string Message);
public sealed record MappingResult(LegacyPayload? Payload, IReadOnlyList<ValidationError> Errors)
{
    public bool IsValid => Payload is not null && Errors.Count == 0;
}

public sealed record LegacyPayload([property: JsonPropertyName("member")] LegacyMember Member);
public sealed record LegacyMember(
    [property: JsonPropertyName("given_name")] string GivenName,
    [property: JsonPropertyName("family_name")] string FamilyName,
    [property: JsonPropertyName("dob")] string DateOfBirth,
    [property: JsonPropertyName("contact")] LegacyContact Contact,
    [property: JsonPropertyName("plan_code")] string PlanCode,
    [property: JsonPropertyName("source")] string Source);
public sealed record LegacyContact([property: JsonPropertyName("email")] string Email);

public static class RegistrationMapper
{
    // The CRM contract is case-sensitive. Unknown fields are intentionally ignored.
    private static readonly IReadOnlyDictionary<string, string> Plans =
        new Dictionary<string, string>(StringComparer.Ordinal)
        { ["Single"] = "S", ["Family"] = "F", ["Couple"] = "C" };

    public static MappingResult Map(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Invalid("body", "A JSON object is required.");

        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException) { return Invalid("body", "The request body must contain valid JSON."); }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Invalid("body", "The request body must be a JSON object.");

            var errors = new List<ValidationError>();
            string? ReadRequired(string field)
            {
                if (!root.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
                {
                    errors.Add(new(field, "This field is required."));
                    return null;
                }
                if (value.ValueKind != JsonValueKind.String)
                {
                    errors.Add(new(field, "This field must be a string."));
                    return null;
                }
                var text = value.GetString()!.Trim();
                if (text.Length > 0) return text;
                errors.Add(new(field, "This field must not be empty."));
                return null;
            }

            var firstName = ReadRequired("firstName");
            var lastName = ReadRequired("lastName");
            var dobText = ReadRequired("dateOfBirth");
            var email = ReadRequired("email");
            var plan = ReadRequired("membershipType");
            DateOnly dob = default;
            if (dobText is not null && !DateOnly.TryParseExact(dobText, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out dob))
                errors.Add(new("dateOfBirth", "Use a valid date in yyyy-MM-dd format."));

            // Plausibility only: this does not verify that the mailbox exists.
            if (email is not null && (!MailAddress.TryCreate(email, out var address)
                || address.Address != email || email.Any(char.IsWhiteSpace)
                || !address.Host.Contains('.') || address.Host.StartsWith('.') || address.Host.EndsWith('.')))
                errors.Add(new("email", "Use a plausible email address, such as name@example.com."));

            string? planCode = null;
            if (plan is not null && !Plans.TryGetValue(plan, out planCode))
                errors.Add(new("membershipType", "Must be Single, Family, or Couple (case-sensitive)."));

            if (errors.Count > 0) return new(null, errors);
            return new(new LegacyPayload(new LegacyMember(firstName!, lastName!,
                dob.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                new LegacyContact(email!), planCode!, "MILKYWAY")), errors);
        }
    }

    public static string ToJson(LegacyPayload payload) => JsonSerializer.Serialize(payload);
    private static MappingResult Invalid(string field, string message) =>
        new(null, new[] { new ValidationError(field, message) });
}
