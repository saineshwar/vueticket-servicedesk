using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketCore.Data.Access.Commands;
using TicketCore.Data.Access.Queries;
using TicketCore.Data.Audit;
using TicketCore.Data.Auth;
using TicketCore.Data.Messaging;
using TicketCore.Data.Organization.Commands;
using TicketCore.Data.Productivity;
using TicketCore.Data.Organization.Queries;
using TicketCore.Data.ServiceDesks.Commands;
using TicketCore.Data.ServiceDesks.Queries;
using TicketCore.Data.Tickets.Commands;
using TicketCore.Data.Tickets.Queries;
using TicketCore.Data.Users.Commands;
using TicketCore.Data.Users.Queries;

namespace TicketCore.Data;

/// <summary>Registers every Query and Command class, as the old ServiceCollection*Extensions did.</summary>
public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddTicketCoreData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TicketDbContext>(o =>
            o.UseSqlServer(configuration.GetConnectionString("Database")));
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

        // Queries
        services.AddScoped<IOrgUnitQueries, OrgUnitQueries>();
        services.AddScoped<IUserQueries, UserQueries>();
        services.AddScoped<IAccessQueries, AccessQueries>();
        services.AddScoped<IRoleQueries, RoleQueries>();
        services.AddScoped<IRoleAssignmentQueries, RoleAssignmentQueries>();
        services.AddScoped<IServiceDeskQueries, ServiceDeskQueries>();
        services.AddScoped<ITicketQueries, TicketQueries>();

        // Commands
        services.AddScoped<IOrgUnitCommand, OrgUnitCommand>();
        services.AddScoped<IUserCommand, UserCommand>();
        services.AddScoped<IRoleCommand, RoleCommand>();
        services.AddScoped<IRoleAssignmentCommand, RoleAssignmentCommand>();
        services.AddScoped<IAuditCommand, AuditCommand>();
        services.AddScoped<IServiceDeskCommand, ServiceDeskCommand>();
        services.AddScoped<ITicketCommand, TicketCommand>();

        services.AddScoped<ICannedResponseStore, CannedResponseStore>();
        services.AddScoped<Sla.ISlaConfigStore, Sla.SlaConfigStore>();
        services.AddScoped<Dashboard.IDashboardQueries, Dashboard.DashboardQueries>();
        services.AddScoped<Engine.IEngineMonitorStore, Engine.EngineMonitorStore>();
        services.AddScoped<Users.IAvailabilityStore, Users.AvailabilityStore>();
        services.AddScoped<MessagingStore>();
        services.AddScoped<INotificationStore>(sp => sp.GetRequiredService<MessagingStore>());
        services.AddScoped<IEmailQueue>(sp => sp.GetRequiredService<MessagingStore>());

        services.AddScoped<RefreshTokenStore>();
        services.AddScoped<IRefreshTokenQueries>(sp => sp.GetRequiredService<RefreshTokenStore>());
        services.AddScoped<IRefreshTokenCommand>(sp => sp.GetRequiredService<RefreshTokenStore>());

        return services;
    }
}
