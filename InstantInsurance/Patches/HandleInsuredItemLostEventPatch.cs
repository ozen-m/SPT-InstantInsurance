using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Services.InRaid;

namespace InstantInsurance.Patches;

[Injectable]
public class HandleInsuredItemLostEventPatch : AbstractPatch
{
    private static LostOnDeathConfig _lostOnDeathConfig = null!;

    public HandleInsuredItemLostEventPatch(LostOnDeathConfig lostOnDeathConfig)
    {
        _lostOnDeathConfig = lostOnDeathConfig;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(LocationLifecycleService), nameof(LocationLifecycleService.HandleInsuredItemLostEvent));
    }

    [PatchPrefix]
    public static void Prefix(PmcData preRaidPmcProfile, EndLocalRaidRequestData request)
    {
        if (_lostOnDeathConfig.WipeOnRaidStart)
        {
            return;
        }
        if (request.LostInsuredItems is null || !request.LostInsuredItems.Any())
        {
            return;
        }

        // Remove items that are found in the players inventory (they weren't lost) so they won't get sent by mail
        var inventoryItemIds = preRaidPmcProfile.Inventory!.Items!.Select(i => i.Id).ToHashSet();
        request.LostInsuredItems = request.LostInsuredItems.Where(lostItem => !inventoryItemIds.Contains(lostItem.Id));
    }
}
