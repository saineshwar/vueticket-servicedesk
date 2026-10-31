using System.Reflection;
using DbUp;
using Microsoft.Extensions.Configuration;

// Applies the numbered scripts in /Scripts to the target database, creating it if needed.
// Connection string: --connection "<value>", env var ConnectionStrings__Database, or user-secrets.
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args, new Dictionary<string, string> { ["--connection"] = "ConnectionStrings:Database" })
    .Build();

var connectionString = configuration.GetConnectionString("Database");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("No connection string. Pass --connection or set ConnectionStrings:Database (user-secrets or environment).");
    return 2;
}

EnsureDatabase.For.SqlDatabase(connectionString);

var upgrader = DeployChanges.To
    .SqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(), name => name.Contains(".Scripts."))
    .WithTransactionPerScript()
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful)
{
    Console.Error.WriteLine(result.Error);
    return 1;
}

Console.WriteLine("Database is up to date.");
return 0;
