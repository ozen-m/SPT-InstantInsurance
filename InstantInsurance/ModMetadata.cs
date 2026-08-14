using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace InstantInsurance;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.ozen.instantinsurance";
    public string Name { get; init; } = "Instant Insurance";
    public string Author { get; init; } = "ozen";
    public List<string>? Contributors { get; init; } = ["Mattdokn", "JustNU"];
    public Version Version { get; init; } = new("1.1.0");
    public Range SptVersion { get; init; } = new("~4.1.2");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; } = 
    [
        "eu.thescrewcollab.equipmentiseternal",
        "com.gorecreek.fairequipmentrestoration",
        "com.blackhorse311.keepstartinggear",
        "com.thecrimsonfuckr.configurablesoftcore",
    ];
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/ozen-m/SPT-InstantInsurance";
    public string License { get; init; } = "MIT";
}
