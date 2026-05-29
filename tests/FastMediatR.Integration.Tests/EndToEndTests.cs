using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FastMediatR.Integration.Tests;

/// <summary>
/// End-to-end integration tests for the FastMediatR.Sample.Api.
/// Each test boots the full ASP.NET Core host via <see cref="WebApplicationFactory{TEntryPoint}"/>
/// and exercises every endpoint through an in-process HTTP client.
/// Verified concerns:
///   • Command dispatch (CreateUserCommand, DeactivateUserCommand)
///   • Query dispatch (GetUserByIdQuery)
///   • Notification fan-out (UserCreatedNotification)
///   • Telemetry demo endpoint exercising traced Send + untraced Publish
///   • FluentValidation pipeline rejection (ValidationBehavior → 400)
///   • Health endpoint liveness probe
/// </summary>
public sealed class EndToEndTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    /// <summary>Initialises the test class using the shared factory fixture.</summary>
    /// <param name="factory">The shared <see cref="WebApplicationFactory{TEntryPoint}"/> fixture.</param>
    public EndToEndTests(CustomWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _client = factory.CreateClient();
    }

    // ── Health ───────────────────────────────────────────────────────────────

    /// <summary>GET /health returns 200 OK with the expected JSON body.</summary>
    [Fact]
    public async Task Health_ReturnsOkWithExpectedBody()
    {
        // Act
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("status").GetString().Should().Be("ok");
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>POST /users with a valid payload dispatches CreateUserCommand and returns 201 Created.</summary>
    [Fact]
    public async Task PostUsers_ValidPayload_Returns201WithId()
    {
        // Arrange
        var payload = new { email = "alice@example.com", displayName = "Alice" };

        // Act
        var response = await _client.PostAsJsonAsync("/users", payload, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
    }

    /// <summary>POST /users with an invalid e-mail is rejected by ValidationBehavior and returns 400.</summary>
    [Fact]
    public async Task PostUsers_InvalidEmail_Returns400WithValidationErrors()
    {
        // Arrange
        var payload = new { email = "not-an-email", displayName = "Alice" };

        // Act
        var response = await _client.PostAsJsonAsync("/users", payload, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.TryGetProperty("errors", out _).Should().BeTrue("the 400 body must include an errors dictionary");
    }

    /// <summary>POST /users with a blank e-mail is rejected by ValidationBehavior and returns 400.</summary>
    [Fact]
    public async Task PostUsers_EmptyEmail_Returns400WithValidationErrors()
    {
        // Arrange
        var payload = new { email = "", displayName = "Alice" };

        // Act
        var response = await _client.PostAsJsonAsync("/users", payload, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("status").GetInt32().Should().Be(400);
    }

    /// <summary>POST /users with a blank display name is rejected by ValidationBehavior and returns 400.</summary>
    [Fact]
    public async Task PostUsers_EmptyDisplayName_Returns400()
    {
        // Arrange
        var payload = new { email = "alice@example.com", displayName = "" };

        // Act
        var response = await _client.PostAsJsonAsync("/users", payload, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>DELETE /users/{id} dispatches DeactivateUserCommand and returns 204 No Content.</summary>
    [Fact]
    public async Task DeleteUsers_ValidId_Returns204()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/users/{id}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    /// <summary>GET /users/{id} with a non-empty GUID dispatches GetUserByIdQuery and returns 200 with the user DTO.</summary>
    [Fact]
    public async Task GetUser_NonEmptyId_Returns200WithDto()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/users/{id}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("id").GetString().Should().Be(id.ToString());
        body.GetProperty("email").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("displayName").GetString().Should().NotBeNullOrEmpty();
    }

    /// <summary>GET /users/{id} with Guid.Empty returns 404 Not Found (handler returns null for empty id).</summary>
    [Fact]
    public async Task GetUser_EmptyGuid_Returns404()
    {
        // Act
        var response = await _client.GetAsync($"/users/{Guid.Empty}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Notifications ─────────────────────────────────────────────────────────

    /// <summary>POST /users/{id}/events/created publishes UserCreatedNotification and returns 202 Accepted.</summary>
    [Fact]
    public async Task PostUserCreatedEvent_ValidIds_Returns202()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _client.PostAsync(
            $"/users/{id}/events/created?email=alice@example.com",
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    /// <summary>GET /telemetry/demo returns 200 and reports that the sample notification was published.</summary>
    [Fact]
    public async Task TelemetryDemo_Returns200WithExpectedBody()
    {
        // Act
        var response = await _client.GetAsync("/telemetry/demo", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("span").GetString().Should().Be("GetUserByIdQuery");
        body.GetProperty("notificationPublished").GetBoolean().Should().BeTrue();
    }
}
