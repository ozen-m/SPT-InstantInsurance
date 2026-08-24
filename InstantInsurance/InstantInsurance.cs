using InstantInsurance.Utils;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Spt.Config;

// ReSharper disable ParameterOnlyUsedForPreconditionCheck.Local

namespace InstantInsurance;

[Injectable(TypePriority = OnLoadOrder.Preload + 1000)]
public class InstantInsurance(
    InstantInsuranceLogger L,
    ItemHelper itemHelper,
    IEnumerable<IRuntimePatch> patches,
    LostOnDeathConfig lostOnDeathConfig
) : IOnLoad
{
    public const string IncompatibleMessage =
        "This mod is incompatible with SPT's `LostOnDeath.WipeOnRaidStart` configuration. Disable `WipeOnRaidStart`, or use DrakiaXYZ's NoCheese mod together with Instant Insurance instead.";

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (lostOnDeathConfig.WipeOnRaidStart)
        {
            L.Error(IncompatibleMessage);
            throw new InvalidOperationException($"[{nameof(InstantInsurance)}] {IncompatibleMessage}");
        }

        CommonExtensions.SetItemHelper(itemHelper);

        foreach (var patch in patches)
        {
            patch.Enable();
        }

        L.Success("loaded successfully!");
        return Task.CompletedTask;
    }
}
