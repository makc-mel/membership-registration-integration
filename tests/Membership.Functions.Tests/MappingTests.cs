using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Membership.Functions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Membership.Functions.Tests;

public class MappingTests
{
    private const string Valid = """
        {"firstName":"Alex","lastName":"Nguyen","dateOfBirth":"1990-04-12",
         "email":"alex.nguyen@example.com","membershipType":"Single",
         "registeredAt":"2026-06-01T09:00:00Z"}
        """;
    private static string With(string field, JsonNode? value)
    {
        var input = JsonNode.Parse(Valid)!.AsObject();
        input[field] = value;
        return input.ToJsonString();
    }

    [Fact]
    public void ValidRegistrationMatchesEntireLegacyContract()
    {
        var result = RegistrationMapper.Map(Valid);
        Assert.True(result.IsValid);
        var expected = JsonNode.Parse("""
            {"member":{"given_name":"Alex","family_name":"Nguyen","dob":"12/04/1990",
            "contact":{"email":"alex.nguyen@example.com"},"plan_code":"S","source":"MILKYWAY"}}
            """);
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(RegistrationMapper.ToJson(result.Payload!))));
    }

    public static IEnumerable<object[]> RequiredFields() =>
        new[] { "firstName", "lastName", "dateOfBirth", "email", "membershipType" }
            .Select(field => new object[] { field });

    [Theory, MemberData(nameof(RequiredFields))]
    public void RequiredFieldsRejectMissingNullEmptyWhitespaceAndWrongType(string field)
    {
        var missing = JsonNode.Parse(Valid)!.AsObject();
        missing.Remove(field);
        AssertFieldError(missing.ToJsonString(), field);
        foreach (var value in new JsonNode?[] { null, JsonValue.Create(""), JsonValue.Create("  "),
            JsonValue.Create(123), new JsonArray(), new JsonObject(), JsonValue.Create(true) })
            AssertFieldError(With(field, value), field);
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("a@")]
    [InlineData("a@example")]
    [InlineData("a b@example.com")]
    [InlineData("Alex <alex@example.com>")]
    public void InvalidEmailReturnsError(string email) => AssertFieldError(With("email", JsonValue.Create(email)), "email");

    [Theory]
    [InlineData("Gold")]
    [InlineData("single")]
    public void UnknownPlanReturnsError(string plan) => AssertFieldError(With("membershipType", JsonValue.Create(plan)), "membershipType");

    [Theory]
    [InlineData("Single", "S")]
    [InlineData("Family", "F")]
    [InlineData("Couple", "C")]
    public void MapsEachPlan(string plan, string expected)
    {
        var result = RegistrationMapper.Map(With("membershipType", JsonValue.Create(plan)));
        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Payload!.Member.PlanCode);
    }

    [Theory]
    [InlineData("1990-04-12", "12/04/1990")]
    [InlineData("2000-02-29", "29/02/2000")]
    public void ConvertsDates(string input, string expected) =>
        Assert.Equal(expected, RegistrationMapper.Map(With("dateOfBirth", JsonValue.Create(input))).Payload!.Member.DateOfBirth);

    [Theory]
    [InlineData("12/04/1990")]
    [InlineData("1990-4-12")]
    [InlineData("1990-04-12T00:00:00Z")]
    [InlineData("2025-02-29")]
    [InlineData("2026-13-01")]
    public void InvalidDateReturnsError(string date) => AssertFieldError(With("dateOfBirth", JsonValue.Create(date)), "dateOfBirth");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("123")]
    public void InvalidBodyReturnsError(string? body) => AssertFieldError(body, "body");

    [Fact]
    public void ExtraNestedFieldIsIgnored()
    {
        var extra = With("unexpected", JsonNode.Parse("{\"anything\":[1,true,null]}"));
        Assert.Equal(RegistrationMapper.ToJson(RegistrationMapper.Map(Valid).Payload!),
            RegistrationMapper.ToJson(RegistrationMapper.Map(extra).Payload!));
    }

    [Fact]
    public void RegisteredAtIsOptionalAndUnmapped()
    {
        var input = JsonNode.Parse(Valid)!.AsObject();
        input.Remove("registeredAt");
        Assert.True(RegistrationMapper.Map(input.ToJsonString()).IsValid);
    }

    [Fact]
    public void ReturnsAllRequiredErrorsTogether()
    {
        var result = RegistrationMapper.Map("{}");
        Assert.False(result.IsValid);
        Assert.Null(result.Payload);
        Assert.Equal(5, result.Errors.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HttpFunctionReturnsExpectedStatusAndBody(bool valid)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(valid ? Valid : "{}"));
        var response = await new MapRegistrationFunction().Run(context.Request, CancellationToken.None);
        if (valid)
        {
            var ok = Assert.IsType<ContentResult>(response);
            Assert.Equal(200, ok.StatusCode);
            Assert.Equal("application/json; charset=utf-8", ok.ContentType);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(ok.Content!),
                JsonNode.Parse(RegistrationMapper.ToJson(RegistrationMapper.Map(Valid).Payload!))));
        }
        else
        {
            var bad = Assert.IsType<BadRequestObjectResult>(response);
            Assert.Equal(400, bad.StatusCode);
            using var body = JsonDocument.Parse(JsonSerializer.Serialize(bad.Value));
            Assert.Equal("validation_failed", body.RootElement.GetProperty("error").GetString());
            Assert.Equal(5, body.RootElement.GetProperty("errors").GetArrayLength());
        }
    }

    private static void AssertFieldError(string? input, string field)
    {
        var result = RegistrationMapper.Map(input);
        Assert.False(result.IsValid);
        Assert.Null(result.Payload);
        Assert.Contains(result.Errors, e => e.Field == field && !string.IsNullOrWhiteSpace(e.Message));
    }
}
