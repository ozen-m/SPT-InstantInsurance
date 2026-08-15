using System.Reflection;
using HarmonyLib;
using InstantInsurance.Utils;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Services.InRaid;

namespace InstantInsurance.Patches;

[Injectable]
public class HandleInsuredItemLostEventPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(LocationLifecycleService), "HandleInsuredItemLostEvent");
    }

    [PatchPrefix]
    public static void Prefix(PmcData preRaidPmcProfile, EndLocalRaidRequestData request)
    {
        if (request.LostInsuredItems is null || !request.LostInsuredItems.Any())
        {
            return;
        }

        // Remove items that are found in the players inventory (they weren't lost)
        // TODO: Conflicts with LostOnDeathConfig.WipeOnRaidStart
        var inventoryItemIds = preRaidPmcProfile.Inventory!.Items!.Select(i => i.Id).ToHashSet();
        request.LostInsuredItems = request.LostInsuredItems.Where(lostItem => !inventoryItemIds.Contains(lostItem.Id));
    }
}
