using InstantInsurance.Configuration;
using InstantInsurance.Tests.Mock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Loaders;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Hosted;
using SPTarkov.Server.Helpers;
using Program = SPTarkov.Server.Program;

namespace InstantInsurance.Tests;

// From sp-tarkov/server-csharp/Testing/UnitTests
// Thank you SPT team!

[SetUpFixture]
public class DI
{
#pragma warning disable NUnit1032
    private static IServiceProvider? _serviceProvider;
#pragma warning restore NUnit1032

    [OneTimeSetUp]
    public void Setup()
    {
        ConfigureServices();
    }

    private static DatabaseTables SetupDB(IReadOnlyDictionary<Type, BaseConfig> configuration, LocaleTable locales, ILogger logger)
    {
        var services = new ServiceCollection();

        services.AddSingleton(locales);
        foreach (var configEntry in configuration)
        {
            services.AddSingleton(configEntry.Key, configEntry.Value);
        }
        services.AddSingleton(logger);
        services.AddSingleton(typeof(ILogger<>), typeof(MockLogger<>));
        services.AddSingleton(typeof(ISptLogger<>), typeof(MockLogger<>));

        var diHandler = new DependencyInjectionHandler(services);
        diHandler.AddInjectableTypesFromAssembly(typeof(Program).Assembly);
        diHandler.AddInjectableTypesFromAssembly(typeof(SPTStartupHostedService).Assembly);
        diHandler.InjectAll();
        services.AddSingleton<DatabaseImporter>();

        var serviceProvider = services.BuildServiceProvider();
        var dbImporter = serviceProvider.GetRequiredService<DatabaseImporter>();
        var tables = dbImporter.LoadDatabaseAsync(false).GetAwaiter().GetResult();

        return tables ?? throw new InvalidOperationException("Tables aren't loaded lol");
    }

    private static void ConfigureServices()
    {
        if (_serviceProvider != null)
        {
            return;
        }

        var mockLogger = new MockLogger<DI>();
        var configuration = ConfigLoader.Initialize(mockLogger).GetAwaiter().GetResult();

        var services = new ServiceCollection();
        services.AddSingleton(mockLogger);
        services.AddSingleton(typeof(ILogger<>), typeof(MockLogger<>));
        services.AddSingleton(typeof(ISptLogger<>), typeof(MockLogger<>));
        services.AddHttpContextAccessor();
        services.AddHttpClient();

        var locales = ProgramHelpers.CreateEarlyLocaleTable() ?? throw new InvalidOperationException("Locales aren't loaded lmao");
        var db = SetupDB(configuration, locales, mockLogger);
        services.AddSingleton(db.Bots);
        services.AddSingleton(db.Hideout);
        services.AddSingleton(db.Locales);
        services.AddSingleton(db.Locations);
        services.AddSingleton(db.Match);
        services.AddSingleton(db.Templates);
        services.AddSingleton(db.Traders);
        services.AddSingleton(db.Globals);
        services.AddSingleton(db.Server);
        services.AddSingleton(db.Settings);

        foreach (var configEntry in configuration)
        {
            services.AddSingleton(configEntry.Key, configEntry.Value);
        }


        var diHandler = new DependencyInjectionHandler(services);


        #region MODS

        diHandler.AddInjectableTypesFromTypeAssembly(typeof(InstantInsurance));

        var instantInsuranceConfig = new InstantInsuranceConfig();
        services.AddSingleton(instantInsuranceConfig);

        #endregion

        diHandler.AddInjectableTypesFromTypeAssembly(typeof(SPTStartupHostedService));

        diHandler.InjectAll();

        services.AddSingleton<IReadOnlyList<SptMod>>(_ => []);

        _serviceProvider = services.BuildServiceProvider();

        var cancellationTokenSource = new CancellationTokenSource();

        foreach (var onLoad in _serviceProvider.GetServices<IOnLoad>())
        {
            onLoad.OnLoadAsync(cancellationTokenSource.Token).Wait(cancellationTokenSource.Token);
        }
    }

    public static T Get<T>() where T : notnull
    {
        return _serviceProvider == null
            ? throw new InvalidOperationException("Service provider not initialized")
            : _serviceProvider.GetRequiredService<T>();
    }
}
