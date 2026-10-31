namespace TicketCore.Common;

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Address people use to open the web app; used for links in emails.</summary>
    public string PublicUrl { get; set; } = "http://localhost:5173";
    public string ProductName { get; set; } = "VueTicket";
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Folder for uploaded files. Relative paths are resolved against the API's content root.</summary>
    public string LocalPath { get; set; } = "App_Data/files";
    public long MaxFileBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxFilesPerUpload { get; set; } = 5;
}

public enum EmailMode
{
    /// <summary>Write .eml files to a folder (development, or when no mail server is available).</summary>
    Pickup,
    Smtp
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailMode Mode { get; set; } = EmailMode.Pickup;

    /// <summary>Runs the background sender in this process. Turned off in tests and on API nodes once the worker host exists.</summary>
    public bool DispatcherEnabled { get; set; } = true;
    public string PickupPath { get; set; } = "App_Data/mail";
    public string FromAddress { get; set; } = "support@localhost";
    public string FromName { get; set; } = "VueTicket";
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool SmtpStartTls { get; set; } = true;
    public string? SmtpUserName { get; set; }

    /// <summary>Set through user-secrets or an environment variable, never in appsettings.json.</summary>
    public string? SmtpPassword { get; set; }
}
