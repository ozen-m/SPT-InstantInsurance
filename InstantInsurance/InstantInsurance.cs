using InstantInsurance.Utils;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;

namespace InstantInsurance;

[Injectable(TypePriority = OnLoadOrder.Preload + 1000)]
public class InstantInsurance(InstantInsuranceLogger L, ItemHelper itemHelper, IEnumerable<IRuntimePatch> patches) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        CommonExtensions.SetItemHelper(itemHelper);

        foreach (var patch in patches)
        {
            patch.Enable();
        }

        L.Success("loaded successfully!");
        return Task.CompletedTask;
    }
}
