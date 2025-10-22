namespace InstantInsurance.Configuration;

public class ModConfig
{
    public bool SimulateItemsBeingTaken { get; set; } = true;
    public bool LoseInsuranceOnItemAfterDeath { get; set; } = true;
    public bool LoseAmmoInMagazines { get; set; } = false;
    public bool DebugLogs { get; set; } = true;
}
