using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TicketCore.Api.Tests;

/// <summary>Checks that the API denies by default. These tests do not need a database.</summary>
public class SecurityDefaultsTests(SecurityDefaultsTests.Factory factory) : IClassFixture<SecurityDefaultsTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Database", "Server=unused;Database=unused;");
            builder.UseSetting("Jwt:SigningKey", "test-signing-key-that-is-long-enough-1234567890");
            builder.UseSetting("Email:DispatcherEnabled", "false");
        }
    }

    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("/api/v1/me")]
    [InlineData("/api/v1/me/permissions")]
    [InlineData("/api/v1/org-units")]
    [InlineData("/api/v1/roles")]
    [InlineData("/api/v1/users")]
    [InlineData("/api/v1/role-assignments")]
    [InlineData("/api/v1/tickets")]
    [InlineData("/api/v1/tickets/1")]
    [InlineData("/api/v1/service-desks")]
    [InlineData("/api/v1/lookups")]
    [InlineData("/api/v1/admin/service-desks")]
    public async Task Protected_endpoints_reject_anonymous_requests(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        // Header and payload are valid JSON; the signature belongs to a different key.
        const string forged = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIiwiaXNzIjoiVnVlVGlja2V0IiwiYXVkIjoiVnVlVGlja2V0IiwiZXhwIjo0MTAyNDQ0ODAwfQ.c2lnbmF0dXJlLWZyb20tYW5vdGhlci1rZXk";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new("Bearer", forged);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_without_cookie_is_unauthorized()
    {
        var response = await _client.PostAsync("/api/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_is_public()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
