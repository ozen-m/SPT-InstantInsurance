# Instant Insurance
Receive your insured items immediately after death!

Prapor and Therapist's teams have been reorganized, they're now faster and efficient in finding your items - delivering straight to your inventory.

A huge thank you to [Mattdokn](https://forge.sp-tarkov.com/user/41131/mattdokn) for allowing me to continue his work on [Insurance Plus](https://forge.sp-tarkov.com/mod/1545/insurance-plus).

### Configuration
In the configuration file `config.json`

- `SimulateItemsBeingTaken` - Items can be taken before the insurer can retrieve your item. Default is `true`. This can be overriden if the server's insurance config `SimulateItemsBeingTaken` is set to `true`
- `LoseInsuranceOnItemAfterDeath` - Lose insurance status on death. Default is `true`
- `LoseAmmoInMagazines` - If disabled, ammo that is chambered or loaded into an insured magazine/weapon that is found will be returned. Default is `false`

### Installation
Extract the contents of the .zip archive into your SPT directory.

### Recommended Mods
- Mods that alter insurance prices, it is recommended to increase insurance prices to balance the changes done by this mod. [TraderQOL](https://forge.sp-tarkov.com/mod/1547/trader-qol) by [Mattdokn](https://forge.sp-tarkov.com/user/41131/mattdokn) provides options for changing insurance prices

### Changes from Insurance Plus
- Simulate items being taken as an option. Some items might still get sent by mail when the container that holds the item is taken, but not the item itself
- Uses SPT methods to simulate items being taken, thank you SPT team!
- Ported to the new SPT C# Server

<br></br>
_**Policy Notice:** Insurance scamming is not tolerated and those dropped in-raid will be processed through the regular insurance system_