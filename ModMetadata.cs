using SPTarkov.Server.Core.Models.Spt.Mod;

namespace InstantInsurance;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.ozen.instantinsurance";
    public override string Name { get; init; } = "Instant Insurance";
    public override string Author { get; init; } = "ozen";
    public override List<string> Contributors { get; init; } = ["Mattdokn", "JustNU"];
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.1");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string> Incompatibilities { get; init; } = [];
    public override Dictionary<string, SemanticVersioning.Range> ModDependencies { get; init; }
    public override string Url { get; init; } = "https://forge.sp-tarkov.com/mod/2394/instant-insurance";
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}