using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using InstantInsurance.Configuration;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Utils.Json.Converters;
using SPTarkov.Server.Web.Models.Configs;
using SPTarkov.Server.Web.Services;

namespace InstantInsurance.Utils;

public class InstantInsuranceOnDIConstruct : IOnDIConstruct
{
    private static readonly JsonSerializerOptions _options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n",
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new StringToMongoIdConverter() },
    };

    public static async Task OnDIConstructAsync(IServiceCollection serviceCollection, CancellationToken cancellationToken)
    {
        var modPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

        var configPath = Path.Combine(modPath, "config", "config.json");
        var config = await LoadAsync<InstantInsuranceConfig>(configPath, cancellationToken);
        serviceCollection.AddSingleton(config);
    }

    private static async Task<T> LoadAsync<T>(string filePath, CancellationToken token = default) where T : new()
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        await using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        return await JsonSerializer.DeserializeAsync<T>(fs, _options, token) ?? new T();
    }
}

[Injectable(InjectionType.Singleton)]
public class ConfigEditorProvider(InstantInsuranceConfig config, ModHelper modHelper) : IConfigEditorConfigProvider
{
    public IEnumerable<ConfigEditorConfigRegistration> GetConfigs()
    {
        var metadata = new ModMetadata();
        var modDir = modHelper.GetAbsolutePathToModFolder();
        yield return ConfigEditorConfigRegistration.Create(
            metadata.ModGuid,
            metadata.Name,
            config,
            Path.Combine(modDir, "config", "config.json")
        );
    }
}
