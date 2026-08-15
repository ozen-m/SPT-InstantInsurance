using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.InRaid;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace InstantInsurance.Patches;

[Injectable]
public class DeleteInventoryPatch : AbstractPatch
{
    private static InstantInsuranceController _controller = null!;

    public DeleteInventoryPatch(
        InstantInsuranceController controller
    )
    {
        _controller = controller;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(InRaidHelper), nameof(InRaidHelper.DeleteInventory));
    }

    [PatchPrefix]
    public static bool Prefix(PmcData pmcData, MongoId sessionId)
    {
        return _controller.ProcessInventory(pmcData, sessionId);
    }
}
