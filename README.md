# Instant Insurance
Receive your insured items immediately after death!

Prapor and Therapist’s teams have been reorganized — they’re now faster and more efficient at recovering your items, delivering them straight to your equipment.

A huge thank you to [Mattdokn](https://forge.sp-tarkov.com/user/41131/mattdokn) for allowing me to continue his work on [Insurance Plus](https://forge.sp-tarkov.com/mod/1545/insurance-plus).

### Configuration
In the configuration file `config.json`

- `SimulateItemsBeingTaken` - If enabled, items can be taken before the insurer can retrieve your item. Default is `true`
- `LoseInsuranceOnItemAfterDeath` - If enabled, insurance status will be lost on death. Default is `true`
- `LoseAmmoInMagazines` - If enabled, ammo that is loaded into an insured magazine will NOT be kept. Default is `false`

### Installation
- Extract the contents of the .zip archive into your SPT directory.

### Recommended Mods
- Mods that alter insurance prices and/or insurance return percentages. It is recommended to increase insurance prices to balance the changes done by this mod. [SVM](https://forge.sp-tarkov.com/mod/236/server-value-modifier-svm) by [GhostFenixx](https://forge.sp-tarkov.com/user/3972/ghostfenixx), and [TraderQOL](https://forge.sp-tarkov.com/mod/1547/trader-qol) by [Mattdokn](https://forge.sp-tarkov.com/user/41131/mattdokn) provides options for changing insurance related options

### Compatibility
- Tested to be compatible with [Project Fika](https://forge.sp-tarkov.com/mod/2326/project-fika)

### Changes from Insurance Plus
- Simulate items being taken as an option. Some items might still get sent by mail when the container that holds the item is taken, but not the item itself
- Uses SPT methods to simulate items being taken, thank you SPT team!
- Ported to the new SPT C# Server

<br></br>
_**Policy Notice:** Insurance scamming is not tolerated. Those items dropped in-raid will be processed through the regular insurance system_