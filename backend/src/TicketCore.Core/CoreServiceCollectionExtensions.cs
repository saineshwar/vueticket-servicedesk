using Microsoft.Extensions.DependencyInjection;
using TicketCore.Core.Access;
using TicketCore.Core.Auth;
using TicketCore.Core.Notifications;
using TicketCore.Core.Organization;
using TicketCore.Core.Productivity;
using TicketCore.Core.ServiceDesks;
using TicketCore.Core.Setup;
using TicketCore.Core.Tickets;
using TicketCore.Core.Users;

namespace TicketCore.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddTicketCoreDomain(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<AccessVersion>();
        services.AddSingleton<OrgTreeCache>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();
        services.AddScoped<IOrgUnitService, OrgUnitService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ISetupService, SetupService>();
        services.AddScoped<IServiceDeskService, ServiceDeskService>();
        services.AddScoped<ITicketService, TicketService>();
        services.AddScoped<ITicketNotifier, TicketNotifier>();
        services.AddScoped<ICannedResponseService, CannedResponseService>();
        services.AddScoped<Sla.ISlaConfigService, Sla.SlaConfigService>();
        services.AddScoped<Dashboard.IDashboardService, Dashboard.DashboardService>();
        services.AddScoped<Engine.IEngineMonitorService, Engine.EngineMonitorService>();
        return services;
    }
}
