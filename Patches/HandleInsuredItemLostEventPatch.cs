using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Services;
using System.Reflection;

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
        if (request.LostInsuredItems is not null && request.LostInsuredItems.Any())
        {
            // Set mapId of the location the raid ended from
            var serverDetails = request.ServerId.Split(".");
            var locationName = serverDetails[0].ToLowerInvariant();
            DeleteInventoryPatch.mapId = locationName;

            // Remove items that are found in the players inventory (they weren't lost)
            request.LostInsuredItems = request.LostInsuredItems.Where(item => !preRaidPmcProfile.Inventory.Items.Select(i => i.Id).Contains(item.Id));
        }
    }
}