using System.Reflection;
using InstantInsurance.Configuration;
using InstantInsurance.Utils;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace InstantInsurance;

[Injectable(TypePriority = OnLoadOrder.PreSptModLoader + 1)]
public class InstantInsurance(
    ISptLogger<InstantInsurance> logger,
    ModHelper modHelper,
    JsonUtil jsonUtil,
    ItemHelper itemHelper,
    PatchManager patchManager
) : IOnLoad
{
    public static ModConfig ModConfig { get; private set; } = new();

    public Task OnLoad()
    {
        LoggerUtil.Logger = logger;
        LoggerUtil.ItemHelper = itemHelper;

        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var configPath = Path.Combine(modPath, "config", "config.json");
        LoadConfig(configPath);

        patchManager.PatcherName = "FoldablesPatcher";
        patchManager.AutoPatch = true;
        patchManager.EnablePatches();

        LoggerUtil.Success("loaded successfully!");
        return Task.CompletedTask;
    }

    private void LoadConfig(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(path);
            }
            ModConfig = jsonUtil.DeserializeFromFile<ModConfig>(path);
            //ModConfig = modHelper.GetJsonDataFromFile<ModConfig>(ConfigPath, "config.json");
        }
        catch (Exception ex)
        {
            LoggerUtil.Error(ex.ToString());
            LoggerUtil.Error("Configuration load error, using default values. Misconfigured config.json? ");
        }
    }
}
