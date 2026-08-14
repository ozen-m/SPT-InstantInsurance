namespace InstantInsurance.Configuration;

public class InstantInsuranceConfig
{
    public bool SimulateItemsBeingTaken { get; set; } = true;
    public bool LoseInsuranceOnItemAfterDeath { get; set; } = true;
    public bool LoseAmmoInMagazines { get; set; } = false;
    public bool DebugLogs { get; set; } = true;
}
