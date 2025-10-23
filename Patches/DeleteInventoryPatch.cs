using HarmonyLib;
using InstantInsurance.Utils;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using System.Collections.Frozen;
using System.Reflection;
using Insurance = SPTarkov.Server.Core.Models.Eft.Profile.Insurance;

namespace InstantInsurance.Patches;

public class DeleteInventoryPatch : AbstractPatch
{
    public static string MapId;

    protected static InsuranceConfig insuranceConfig;
    protected static InsuranceController insuranceController;
    protected static ItemHelper itemHelper;
    protected static InventoryHelper inventoryHelper;
    protected static TraderHelper traderHelper;
    protected static TimeUtil timeUtil;
    protected static DatabaseService databaseService;
    protected static RandomUtil randomUtil;

    private static MethodInfo getInventoryItemsLostOnDeathMethod;
    private static MethodInfo findItemsToDeleteMethod;
    private static MethodInfo sendMailMethod;

    protected override MethodBase GetTargetMethod()
    {
        insuranceConfig = ServiceLocator.ServiceProvider.GetRequiredService<ConfigServer>().GetConfig<InsuranceConfig>();
        insuranceController = ServiceLocator.ServiceProvider.GetRequiredService<InsuranceController>();
        itemHelper = ServiceLocator.ServiceProvider.GetRequiredService<ItemHelper>();
        inventoryHelper = ServiceLocator.ServiceProvider.GetRequiredService<InventoryHelper>();
        traderHelper = ServiceLocator.ServiceProvider.GetRequiredService<TraderHelper>();
        timeUtil = ServiceLocator.ServiceProvider.GetRequiredService<TimeUtil>();
        databaseService = ServiceLocator.ServiceProvider.GetRequiredService<DatabaseService>();
        randomUtil = ServiceLocator.ServiceProvider.GetRequiredService<RandomUtil>();

        getInventoryItemsLostOnDeathMethod = AccessTools.Method(typeof(InRaidHelper), "GetInventoryItemsLostOnDeath");
        findItemsToDeleteMethod = AccessTools.Method(typeof(InsuranceController), "FindItemsToDelete");
        sendMailMethod = AccessTools.Method(typeof(InsuranceController), "SendMail");

        return AccessTools.Method(typeof(InRaidHelper), nameof(InRaidHelper.DeleteInventory));
    }

    [PatchPrefix]

    protected static bool Prefix(InRaidHelper __instance, PmcData pmcData, MongoId sessionId)
    {
        Dictionary<MongoId, Insurance> insuranceTraders = [];
        HashSet<Item> itemsProcessed = [];
        HashSet<Item> itemsAmmo = [];
        HashSet<MongoId> itemsToUninsure = [];
        HashSet<MongoId> itemsToDelete = [];
        int itemsKeptByInsurance = 0;
        int itemsSentByMail = 0;

        // Get inventory item ids to remove from players profile
        IEnumerable<Item> itemsLostOnDeath = (IEnumerable<Item>)getInventoryItemsLostOnDeathMethod.Invoke(__instance, [pmcData]);
        foreach (Item item in itemsLostOnDeath)
        {
            var itemAndChildrenLostOnDeath = pmcData.Inventory.Items.GetItemWithChildren(item.Id);
            foreach (var child in itemAndChildrenLostOnDeath)
            {
                itemsProcessed.Add(child);
                var insuredItem = GetInsuredItem(pmcData, child.Id);
                if (insuredItem is not null)
                {
                    if (insuranceTraders.TryGetValue(insuredItem.TId, out var insurance))
                    {
                        if (itemsToUninsure.Add(child.Id))
                        {
                            insurance.Items.Add(child);
                        }
                    }
                    else
                    {
                        itemsToUninsure.Add(child.Id);

                        // Create new insurance package for trader
                        insuranceTraders[insuredItem.TId] = new()
                        {
                            TraderId = insuredItem.TId,
                            Items = [child],
                        };
                    }
                }
                else
                {
                    if (ShouldKeepAmmo(child))
                    {
                        itemsAmmo.Add(child);

                        if (!InstantInsurance.ModConfig.SimulateItemsBeingTaken)
                        {
                            continue;
                        }
                    }
                    itemsToDelete.Add(child.Id);
                }
            }
        }

        if (InstantInsurance.ModConfig.SimulateItemsBeingTaken)
        {
            // Get all items to delete before processing insurance package to be sent, which will check if an item's parent will be deleted
            foreach (var (_, insurance) in insuranceTraders)
            {
                // Find items that could be taken by another player off the players body, using SPT's method
                var foundItemsToDelete = (HashSet<MongoId>)findItemsToDeleteMethod.Invoke(insuranceController, [pmcData.Inventory.Equipment.ToString(), insurance]);
                itemsToDelete.UnionWith(foundItemsToDelete ?? Enumerable.Empty<MongoId>());
            }

            var itemsMap = itemsProcessed.GenerateItemsMap();
            foreach (var (_, insurance) in insuranceTraders)
            {
                // Remove items from the insured items that should not be returned to the player
                insurance.Items = [.. insurance.Items.Where(item => !itemsToDelete.Contains(item.Id))];

                // Get ammo from magazines still existing, add it to the insurance package
                var insuranceItemsIds = insurance.Items.Select(i => i.Id);
                var ammoWithAliveParents = itemsAmmo
                    .Where(a => insuranceItemsIds.Contains(a.ParentId));

                // Add it to insurance and keep ammo items from being deleted
                insurance.Items.AddRange(ammoWithAliveParents);
                itemsToDelete.ExceptWith(ammoWithAliveParents.Select(a => a.Id));

                itemsKeptByInsurance += insurance.Items.Count;

                // Update insurance package that will be sent by mail
                var itemsToSend = new List<Item>();
                foreach (var insured in insurance.Items)
                {
                    // Get item parents, then check if any will be removed, if so send the item by mail
                    var parentsIds = GetItemParentsIds(insured.Id, itemsMap);
                    if (itemsToDelete.Any(i => parentsIds.Contains(i)))
                    {
                        itemsToSend.Add(insured);
                        //LoggerUtil.Debug($"Item {insured.Name()} {insured.Id} has a parent that will be removed, adopting");
                    }
                }
                insurance.Items = itemsToSend;

                if (insurance.Items.Count > 0)
                {
                    itemsSentByMail += insurance.Items.Count;

                    // Create a new root parent ID for the message we'll be sending the player
                    var mailRootItemParentId = new MongoId();

                    // Populate remaining insurance details, here just to save cycles if no items?
                    var traderBase = traderHelper.GetTrader(insurance.TraderId, sessionId);
                    var maxInsuranceStorageTime =
                        insuranceConfig.StorageTimeOverrideSeconds > 0
                        ? insuranceConfig.StorageTimeOverrideSeconds
                        : timeUtil.GetHoursAsSeconds((int)traderBase.Insurance.MaxStorageTime);
                    var systemData = new SystemData
                    {
                        Date = timeUtil.GetBsgDateMailFormat(),
                        Time = timeUtil.GetBsgTimeMailFormat(),
                        Location = MapId,
                    };
                    var dialogueTemplates = databaseService.GetTrader(insurance.TraderId).Dialogue;

                    insurance.MaxStorageTime = (int)maxInsuranceStorageTime;
                    insurance.SystemData = systemData;
                    insurance.MessageType = MessageType.InsuranceReturn;
                    insurance.MessageTemplateId = randomUtil.GetArrayValue(dialogueTemplates["insuranceFound"]);
                    insurance.Items = insurance.Items.AdoptOrphanedItems(mailRootItemParentId);

                    sendMailMethod.Invoke(insuranceController, [sessionId, insurance]);
                }
            }
        }

        // Remove itemsToDelete from inventory
        foreach (var itemId in itemsToDelete)
        {
            inventoryHelper.RemoveItem(pmcData, itemId, sessionId);
        }

        // Remove items from insurance
        if (InstantInsurance.ModConfig.LoseInsuranceOnItemAfterDeath)
        {
            pmcData.InsuredItems = [.. pmcData.InsuredItems.Where(insuredItem => !itemsToUninsure.Contains((MongoId)insuredItem.ItemId))];
        }
        else
        {
            itemsToUninsure.Clear();
        }

        LoggerUtil.Debug($"--------");
        LoggerUtil.Debug($"Player: {pmcData.Info.Nickname}"); // Fika
        LoggerUtil.Debug($"Items processed: {itemsProcessed.Count}");
        LoggerUtil.Debug($"Items kept: {itemsKeptByInsurance}");
        LoggerUtil.Debug($"Items removed: {itemsToDelete.Count}");
        LoggerUtil.Debug($"Items uninsured: {itemsToUninsure.Count}");
        LoggerUtil.Debug($"Items sent by mail: {itemsSentByMail}");
        LoggerUtil.Debug($"--------");

        // Remove contents of fast panel
        pmcData.Inventory.FastPanel = [];

        return false;
    }

    protected static bool ShouldKeepAmmo(Item item)
    {
        if (InstantInsurance.ModConfig.LoseAmmoInMagazines)
        {
            return false;
        }
        return itemHelper.IsOfBaseclass(item.Template, BaseClasses.AMMO) && MagazineSlotIds.Contains(item.SlotId);
    }

    /// <summary>
    /// Return the player's insured item from <seealso cref="BotBase.InsuredItems"/>
    /// </summary>
    /// <param name="data">PmcData</param>
    /// <param name="lostItemId">Insured item id to look for</param>
    /// <returns></returns>
    public static InsuredItem GetInsuredItem(BotBase data, MongoId lostItemId)
    {
        return data.InsuredItems?.FirstOrDefault(insuredItem => insuredItem.ItemId == lostItemId);
    }

    /// <summary>
    /// Modified <seealso cref="ItemHelper.GetEquipmentParent"/> to return a list of parents<br></br><br></br>
    /// 
    /// Retrieves the equipment parent item for a given item.<br></br><br></br>
    ///
    /// This method traverses up the hierarchy of items starting from a given `itemId`, until it finds the equipment
    /// parent item. In other words, if you pass it an item id of a suppressor, it will traverse up the muzzle brake,
    /// barrel, upper receiver, gun, nested backpack, and finally return the backpack Item that is equipped.<br></br><br></br>
    ///
    /// It's important to note that traversal is expensive, so this method requires that you pass it a Dictionary of the items
    /// to traverse, where the keys are the item IDs and the values are the corresponding Item objects. This alleviates
    /// some of the performance concerns, as it allows for quick lookups of items by ID.
    /// </summary>
    /// <param name="itemId">The unique identifier of the item for which to find the equipment parent.</param>
    /// <param name="itemsMap">A Dictionary containing item IDs mapped to their corresponding Item objects for quick lookup.</param>
    /// <returns>A list of parents item ids</returns>
    public static List<MongoId> GetItemParentsIds(MongoId itemId, Dictionary<MongoId, Item> itemsMap)
    {
        var parentResults = new List<MongoId>();
        var currentItem = itemsMap.GetValueOrDefault(itemId);

        while (currentItem is not null && !EquipmentSlotsAsStrings.Contains(currentItem.SlotId ?? string.Empty))
        {
            currentItem = itemsMap.GetValueOrDefault(currentItem.ParentId ?? string.Empty);
            if (currentItem is null)
            {
                break;
            }
            else
            {
                parentResults.Add(currentItem.Id);
            }
        }

        return parentResults;
    }

    public static readonly FrozenSet<string> MagazineSlotIds = ["cartridges", "patron_in_weapon", "patron_in_weapon_000", "patron_in_weapon_001"];

    public static readonly FrozenSet<string> EquipmentSlotsAsStrings =
    [
        nameof(EquipmentSlots.Headwear),
        nameof(EquipmentSlots.Earpiece),
        nameof(EquipmentSlots.FaceCover),
        nameof(EquipmentSlots.ArmorVest),
        nameof(EquipmentSlots.Eyewear),
        nameof(EquipmentSlots.ArmBand),
        nameof(EquipmentSlots.TacticalVest),
        nameof(EquipmentSlots.Pockets),
        nameof(EquipmentSlots.Backpack),
        nameof(EquipmentSlots.SecuredContainer),
        nameof(EquipmentSlots.FirstPrimaryWeapon),
        nameof(EquipmentSlots.SecondPrimaryWeapon),
        nameof(EquipmentSlots.Holster),
        nameof(EquipmentSlots.Scabbard),
    ];
}