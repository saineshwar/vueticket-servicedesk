using NLog.Web;
using TicketCore.Common;
using TicketCore.Core;
using TicketCore.Data;
using TicketCore.Engine;
using TicketCore.Services;
using TicketCore.Worker;

// TicketCore.Worker: the independent automation engine.
// It shares the database (data only) and libraries with the API, makes no calls to the API, and can be deployed,
// restarted and scaled on its own: as a Windows service, a Linux systemd service, a container or a console app.
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(o => o.ServiceName = "VueTicket Engine");
builder.Host.UseSystemd();
builder.Logging.ClearProviders();
builder.Host.UseNLog();

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));
builder.Services.AddScoped<ICurrentUser, SystemCurrentUser>();

builder.Services.AddTicketCoreData(builder.Configuration);
builder.Services.AddTicketCoreDomain();

// Registered before the engine so a database that is too old stops the worker before any task starts.
builder.Services.AddHostedService<SchemaCompatibilityCheck>();
builder.Services.AddTicketCoreEngine(builder.Configuration);

// Outgoing email is sent from here as well (Email:DispatcherEnabled).
builder.Services.AddTicketCoreServices(builder.Configuration, builder.Environment.ContentRootPath);

builder.Services.AddHealthChecks();

var app = builder.Build();

// Liveness for load balancers / service monitors, and per-task status for operators.
app.MapHealthChecks("/health");
app.MapGet("/status", (EngineStatus status, EngineInstance instance) => Results.Ok(new
{
    instance = instance.Id,
    tasks = status.Snapshot()
}));

app.Run();
