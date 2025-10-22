using InstantInsurance.Configuration;
using InstantInsurance.Patches;
using InstantInsurance.Utils;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using System.Reflection;

namespace InstantInsurance;

[Injectable(TypePriority = OnLoadOrder.PreSptModLoader + 1)]
public class InstantInsurance(
    ISptLogger<InstantInsurance> logger,
    ModHelper modHelper,
    JsonUtil jsonUtil,
    ItemHelper itemHelper
    ) : IOnLoad
{
    public static ModConfig ModConfig { get; protected set; } = new();

    public async Task OnLoad()
    {
        LoggerUtil.Logger = logger;
        LoggerUtil.ItemHelper = itemHelper;

        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var configPath = Path.Combine(modPath, "config", "config.json");
        await LoadConfig(configPath);

        new HandleInsuredItemLostEventPatch().Enable();
        new DeleteInventoryPatch().Enable();

        LoggerUtil.Success("loaded successfully!");
    }

    private async Task LoadConfig(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(path);
            }
            ModConfig = await jsonUtil.DeserializeFromFileAsync<ModConfig>(path);
            //ModConfig = modHelper.GetJsonDataFromFile<ModConfig>(ConfigPath, "config.json");
        }
        catch (Exception ex)
        {
            LoggerUtil.Error(ex.ToString());
            LoggerUtil.Error("Configuration load error, using default values. Misconfigured config.json? ");
        }
    }
}
