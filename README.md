# Instant Insurance
Receive your insured items immediately after death!

Prapor and Therapist's teams have been reorganized — they're now faster and more efficient at recovering your items, delivering them straight to your equipment.
___

_**NOTICE**_: This mod processes insurance immediately _**ONLY AFTER DEATH**_. When surviving a raid, your items dropped in-raid goes through the _regular_ insurance system.

_**NOTICE**_: Be sure to _insure_ the items you want to keep.
___

### Configuration
In the configuration file `config.json`

- `SimulateItemsBeingTaken` - If enabled, items can be taken before the insurer can retrieve your item. Default is `true`
- `LoseInsuranceOnItemAfterDeath` - If enabled, insurance status will be lost on death. Default is `true`
- `LoseAmmoInMagazines` - If enabled, ammo that is loaded into an insured magazine will NOT be kept. Default is `false`

### Installation
- Extract the contents of the .zip archive into your SPT directory.
<details>
  <summary>Demonstration</summary>

![Installation](https://i.imgur.com/3N6gTe2.gif)
Thank you [DrakiaXYZ](https://forge.sp-tarkov.com/user/27605/drakiaxyz) for the gif
</details>

### Recommended Mods
- Mods that alter insurance prices and/or insurance return percentages. It is recommended to increase insurance prices to balance the changes done by this mod. [SVM](https://forge.sp-tarkov.com/mod/236/server-value-modifier-svm) by [GhostFenixx](https://forge.sp-tarkov.com/user/3972/ghostfenixx), and [TraderQOL](https://forge.sp-tarkov.com/mod/1547/trader-qol) by [Mattdokn](https://forge.sp-tarkov.com/user/41131/mattdokn) provides options for changing insurance related options

### Compatibility
- Tested to be compatible with [Project Fika](https://forge.sp-tarkov.com/mod/2326/project-fika)

### Frequently Asked Questions
<details>
  <summary>FAQs</summary>

#### Why am I losing ___some___ of my items even though `SimulateItemsBeingTaken` is `false`?
- Insurance is processed immediately _**ONLY AFTER DEATH**_. When surviving a raid, your items dropped in-raid goes through the _regular_ insurance system
- You can still lose items if they're uninsured
- Inspect your server logs, they contain information on how your items were processed after the raid ends

#### I'm still losing my items in laboratory, is there a way to change this?
- `SimulateItemsBeingTaken` is overridden if insurance is disabled on the map. You can use [SVM](https://forge.sp-tarkov.com/mod/236/server-value-modifier-svm) by [GhostFenixx](https://forge.sp-tarkov.com/user/3972/ghostfenixx) to enable insurance inside laboratory, `Raid Settings -> Misc -> Working insurance inside Laboratory` is checked
</details>

### Changes from Insurance Plus
A huge thank you to [Mattdokn](https://forge.sp-tarkov.com/user/41131/mattdokn) for allowing me to continue his work on [Insurance Plus](https://forge.sp-tarkov.com/mod/1545/insurance-plus)
- Simulate items being taken as an option. Some items might still get sent by mail when the container that holds the item is taken, but not the item itself
- Uses SPT methods to simulate items being taken, thank you SPT team!
- Ported to the new SPT C# Server

<br></br>
_**Policy Notice:** Insurance scamming is not tolerated. Those items dropped in-raid will be processed through the regular insurance system._
