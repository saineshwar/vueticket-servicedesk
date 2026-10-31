using TicketCore.Core;
using TicketCore.Data.Engine;

namespace TicketCore.Worker;

/// <summary>The engine acts as the system, not as a signed-in person.</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public long? UserId => null;
    public string? IpAddress => null;
}

/// <summary>
/// Refuses to start against a database that is missing migrations this engine version needs, so the API, the
/// database and the worker can be upgraded in any order without the engine running on the wrong schema.
/// </summary>
public sealed class SchemaCompatibilityCheck(IServiceScopeFactory scopes, ILogger<SchemaCompatibilityCheck> logger) : IHostedService
{
    /// <summary>The newest migration this build of the engine depends on.</summary>
    public const string RequiredScript = "TicketCore.Database.Scripts.0006_EngineMonitoring.sql";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var applied = await scope.ServiceProvider.GetRequiredService<IEngineStore>().GetAppliedScriptsAsync(cancellationToken);
        if (!applied.Contains(RequiredScript))
        {
            logger.LogCritical("The database is missing migration {Script}. Run TicketCore.Database before starting the engine.", RequiredScript);
            throw new InvalidOperationException($"Database schema is too old for this engine: {RequiredScript} has not been applied.");
        }
        logger.LogInformation("Database schema is compatible ({Count} migrations applied)", applied.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
