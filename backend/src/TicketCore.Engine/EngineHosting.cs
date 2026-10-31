using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketCore.Data.Engine;
using TicketCore.Engine.Assignment;
using TicketCore.Engine.Housekeeping;
using TicketCore.Engine.Sla;

namespace TicketCore.Engine;

/// <summary>Runs one engine task on its own interval. A failed run is logged and retried next interval.</summary>
public sealed class EngineLoop<TTask>(
    string name,
    Func<EngineOptions, EngineTaskOptions> select,
    IServiceScopeFactory scopes,
    IOptions<EngineOptions> options,
    EngineStatus status,
    EngineInstance instance,
    TimeProvider clock,
    ILogger<EngineLoop<TTask>> logger) : BackgroundService where TTask : IEngineTask
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = select(options.Value);
        status.Registered(name, settings.Enabled);
        await MonitorAsync(m => m.RegisterTaskAsync(instance.Id, name, settings.Enabled, settings.IntervalSeconds, instance.StartedAt, stoppingToken));
        if (!settings.Enabled)
        {
            logger.LogInformation("Engine task {Task} is disabled in configuration", name);
            return;
        }

        logger.LogInformation("Engine task {Task} runs every {Seconds} s", name, settings.IntervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, settings.IntervalSeconds)));
        do
        {
            var startedAt = clock.GetUtcNow().UtcDateTime;
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var processed = await scope.ServiceProvider.GetRequiredService<TTask>().RunOnceAsync(stoppingToken);
                status.Succeeded(name, clock.GetUtcNow().UtcDateTime, processed);
                if (processed > 0) logger.LogInformation("{Task}: {Count} processed", name, processed);
                await MonitorAsync(m => m.RecordRunAsync(new EngineRunRecord(instance.Id, name, startedAt, (int)elapsed.ElapsedMilliseconds, processed, null), stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                status.Failed(name, clock.GetUtcNow().UtcDateTime, ex.Message);
                logger.LogError(ex, "Engine task {Task} failed; retrying next interval", name);
                await MonitorAsync(m => m.RecordRunAsync(new EngineRunRecord(instance.Id, name, startedAt, (int)elapsed.ElapsedMilliseconds, 0, $"{ex.GetType().Name}: {ex.Message}"), stoppingToken));
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Run history for the admin Engine page. Best effort: if it can't be written, the work itself still counts.</summary>
    private async Task MonitorAsync(Func<IEngineMonitorStore, Task> write)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await write(scope.ServiceProvider.GetRequiredService<IEngineMonitorStore>());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not record run history for {Task}", name);
        }
    }
}

/// <summary>On a normal shutdown, hands the event stream back at once instead of making the next worker wait for the lease.</summary>
public sealed class StreamLeaseRelease(IServiceScopeFactory scopes, EngineInstance instance, ILogger<StreamLeaseRelease> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IEngineStore>()
                .ReleaseStreamLeaseAsync(SlaEventProcessor.StreamName, instance.Id, cancellationToken);
            logger.LogInformation("Released the event stream lease");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not release the event stream lease; it will expire on its own");
        }
    }
}

public static class EngineServiceCollectionExtensions
{
    /// <summary>Registers the engine's tasks and their loops. Used by the independent worker host only.</summary>
    public static IServiceCollection AddTicketCoreEngine(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EngineOptions>(configuration.GetSection(EngineOptions.SectionName));
        services.AddSingleton<EngineInstance>();
        services.AddSingleton<EngineStatus>();
        services.AddSingleton<ReferenceData>();

        services.AddScoped<IEngineStore, EngineStore>();
        services.AddScoped<EscalationOwners>();
        services.AddHostedService<StreamLeaseRelease>();

        AddTask<SlaEventProcessor>(services, "sla-events", o => o.Events);
        AddTask<SlaMonitor>(services, "sla-monitor", o => o.SlaMonitor);
        AddTask<EscalationMonitor>(services, "escalation", o => o.Escalation);
        AddTask<AssignmentEngine>(services, "assignment", o => o.Assignment);
        AddTask<UnassignedWatch>(services, "unassigned-watch", o => o.UnassignedWatch);
        AddTask<AutoClose>(services, "auto-close", o => o.AutoClose);
        AddTask<RunLogCleanup>(services, "log-cleanup", o => o.LogCleanup);
        return services;
    }

    private static void AddTask<TTask>(IServiceCollection services, string name, Func<EngineOptions, EngineTaskOptions> select)
        where TTask : class, IEngineTask
    {
        services.AddScoped<TTask>();
        services.AddSingleton<IHostedService>(sp => new EngineLoop<TTask>(
            name, select,
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptions<EngineOptions>>(),
            sp.GetRequiredService<EngineStatus>(),
            sp.GetRequiredService<EngineInstance>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<EngineLoop<TTask>>>()));
    }
}
