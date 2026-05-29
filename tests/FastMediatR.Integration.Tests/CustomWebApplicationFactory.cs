using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FastMediatR.Integration.Tests;

/// <summary>
/// Custom <see cref="WebApplicationFactory{TEntryPoint}"/> that overrides the hosting environment
/// and configuration so the tests run in isolation from any developer machine settings.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Configures the web host for integration testing:
    ///   • Sets the environment to "Testing" so the developer exception page is suppressed.
    ///   • Overrides FastMediatR options via in-memory configuration to keep tests deterministic.
    /// </summary>
    /// <param name="builder">The web host builder provided by the base class.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            // Use a 5 000 ms threshold so no test accidentally triggers the performance warning.
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FastMediatR:PerformanceThresholdMs"] = "5000",
                ["FastMediatR:EnableLogging"] = "true",
                ["FastMediatR:EnablePayloadLogging"] = "false",
            });
        });
    }
}
