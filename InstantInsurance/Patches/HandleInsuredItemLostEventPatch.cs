using System.Reflection;
using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Services;

namespace InstantInsurance.Patches;

public class HandleInsuredItemLostEventPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(LocationLifecycleService), "HandleInsuredItemLostEvent");
    }

    [PatchPrefix]
    public static void Prefix(PmcData preRaidPmcProfile, EndLocalRaidRequestData request)
    {
        // Set mapId of the location the raid ended from
        var serverDetails = request.ServerId!.Split(".");
        var locationName = serverDetails[0].ToLowerInvariant();
        DeleteInventoryPatch.MapId = locationName;
        
        if (request.LostInsuredItems is null || !request.LostInsuredItems.Any()) return;

        // Remove items that are found in the players inventory (they weren't lost)
        var inventoryItemIds = preRaidPmcProfile.Inventory!.Items!.Select(i => i.Id).ToHashSet();
        request.LostInsuredItems = request.LostInsuredItems.Where(lostItem => !inventoryItemIds.Contains(lostItem.Id));
    }
}
