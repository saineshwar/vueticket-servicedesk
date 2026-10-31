using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketCore.Common;
using TicketCore.Services.Email;
using TicketCore.Services.Storage;

namespace TicketCore.Services;

public static class ServicesServiceCollectionExtensions
{
    /// <param name="contentRoot">Relative storage and pickup paths are resolved against this folder.</param>
    public static IServiceCollection AddTicketCoreServices(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .PostConfigure(o => o.LocalPath = Path.GetFullPath(o.LocalPath, contentRoot));
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .PostConfigure(o => o.PickupPath = Path.GetFullPath(o.PickupPath, contentRoot))
            .Validate(o => o.Mode != EmailMode.Smtp || !string.IsNullOrWhiteSpace(o.SmtpHost), "Email:SmtpHost is required when Email:Mode is Smtp.")
            .ValidateOnStart();

        services.AddSingleton<IFileStorage, LocalFileStorage>();

        var mode = configuration.GetSection(EmailOptions.SectionName).GetValue<EmailMode?>(nameof(EmailOptions.Mode)) ?? EmailMode.Pickup;
        if (mode == EmailMode.Smtp) services.AddSingleton<IEmailTransport, SmtpTransport>();
        else services.AddSingleton<IEmailTransport, PickupDirectoryTransport>();

        if (configuration.GetSection(EmailOptions.SectionName).GetValue(nameof(EmailOptions.DispatcherEnabled), true))
            services.AddHostedService<EmailDispatcher>();
        return services;
    }
}
