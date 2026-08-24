using System.Collections.Frozen;
using InstantInsurance.Configuration;
using InstantInsurance.Utils;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.InRaid;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Utils;
using Insurance = SPTarkov.Server.Core.Models.Eft.Profile.Insurance;

namespace InstantInsurance;

[Injectable]
public class InstantInsuranceController(
    InstantInsuranceLogger L,
    InstantInsuranceConfig config,
    InsuranceConfig insuranceConfig,
    LostOnDeathConfig lostOnDeathConfig,
    InsuranceController insuranceController,
    ProfileActivityService profileActivityService,
    InRaidHelper inRaidHelper,
    ItemHelper itemHelper,
    TraderHelper traderHelper,
    TakeItemsHelper takeItemsHelper,
    TradersTable tradersTable,
    LocationTable locationTable,
    TimeUtil timeUtil,
    RandomUtil randomUtil
)
{
    public bool ProcessInventory(PmcData pmcData, MongoId sessionId)
    {
        if (lostOnDeathConfig.WipeOnRaidStart)
        {
            L.Error(InstantInsurance.IncompatibleMessage);
            return true;
        }
        if (pmcData.Inventory is null)
        {
            L.Error("PmcData.Inventory is null when trying to process inventory, falling back to SPT");
            return true;
        }
        if (pmcData.Inventory.Items is null)
        {
            L.Error("PmcData.Inventory.Items is null when trying to process inventory, falling back to SPT");
            return true;
        }
        var mapId = profileActivityService.GetProfileActivityRaidData(sessionId).RaidConfiguration?.Location;
        if (string.IsNullOrEmpty(mapId))
        {
            L.Error("Could not find raid activity for profile, falling back to SPT");
            return true;
        }

        // Classify items lost on death
        Dictionary<MongoId, Insurance> packages = [];
        Dictionary<MongoId, MongoId> tradersMap = [];
        List<Item> insuredItems = [];
        List<Item> ammoToKeep = [];
        HashSet<MongoId> itemsToDelete = [];

        var itemsProcessed = GetAllItemsLostOnDeath(inRaidHelper, pmcData);
        foreach (var child in itemsProcessed)
        {
            var insuredItem = GetInsuredItem(pmcData, child.Id);
            if (insuredItem is not null)
            {
                if (!packages.TryGetValue(insuredItem.TId, out var package))
                {
                    // Create new insurance package for trader
                    package = new Insurance { TraderId = insuredItem.TId, Items = [], };
                    packages[insuredItem.TId] = package;
                }

                package.Items!.Add(child);
                insuredItems.Add(child); 
                tradersMap.Add(child.Id, insuredItem.TId);
                continue;
            }

            if (ShouldKeepAmmo(child))
            {
                ammoToKeep.Add(child);
                // Add to items to delete initially
            }

            itemsToDelete.Add(child.Id);
        }

        // Simulate items being taken before processing insurance package to be sent, which will check if an item's parent will be deleted
        if (config.SimulateItemsBeingTaken)
        {
            // Find items that could be taken by another player off the players body
            var foundItemsToDelete = takeItemsHelper.FindItemsToDelete(insuredItems, tradersMap);
            itemsToDelete.UnionWith(foundItemsToDelete);
        }

        // Get items to be sent by mail. These items got their parents removed
        SendOrphanedItemsByMail(
            packages,
            itemsProcessed,
            ammoToKeep,
            itemsToDelete,
            mapId,
            sessionId,
            out var itemsRemained,
            out var itemsSentByMail
        );

        // Remove itemsToDelete from inventory
        RemoveItemsFromInventory(pmcData, itemsToDelete);

        // Remove items from insurance
        if (config.LoseInsuranceOnItemAfterDeath)
        {
            RemoveInsuranceFromItems(pmcData, insuredItems);
        }

        LogAfterProcessing(
            pmcData.Info?.Nickname,
            mapId,
            itemsProcessed.Count,
            insuredItems.Count,
            itemsRemained,
            itemsSentByMail,
            itemsToDelete.Count
        );

        // Remove contents of fast panel
        pmcData.Inventory.FastPanel = [];

        return false;
    }

    private void SendOrphanedItemsByMail(
        Dictionary<MongoId, Insurance> packages,
        HashSet<Item> itemsProcessed,
        List<Item> ammoToKeep,
        HashSet<MongoId> itemsToDelete,
        string mapId,
        MongoId sessionId,
        out int itemsRemained,
        out int itemsSentByMail
    )
    {
        var itemsMap = itemsProcessed.GenerateItemsMap();
        var locationInsuranceDisabled = IsLocationInsuranceDisabled(mapId);
        itemsRemained = 0;
        itemsSentByMail = 0;
        foreach (var (_, insurance) in packages)
        {
            // Remove items from the insured items that should not be returned to the player
            insurance.Items = [.. insurance.Items!.Where(i => !itemsToDelete.Contains(i.Id))];

            // Get ammo from magazines still existing
            // Add it to the insurance package and remove from items to delete
            var itemIds = insurance.Items.Select(i => i.Id).ToHashSet();
            var ammoWithParents = ammoToKeep.Where(a => itemIds.Contains(a.ParentId ?? MongoId.Empty())).ToArray();
            itemsToDelete.ExceptWith(ammoWithParents.Select(i => i.Id));
            insurance.Items.AddRange(ammoWithParents);

            // Let the mail handle insurance message for disabled maps, so it is apparent that insurance is disabled on that map.
            // Else, replace insurance package with items (whose parents are deleted) to be sent by mail.
            if (locationInsuranceDisabled)
            {
                itemsToDelete.UnionWith(insurance.Items.Select(i => i.Id));
            }
            else
            {
                itemsRemained += insurance.Items.Count;

                insurance.Items =
                [
                    .. insurance.Items.Where(item =>
                        {
                            // Get item parents, then check if any will be removed, if so send the item by mail
                            var parentsIds = GetItemParentsIds(item.Id, itemsMap);
                            return itemsToDelete.Any(parentsIds.Contains);
                        }
                    ),
                ];
                // Subtract items sent by mail
                itemsRemained -= insurance.Items.Count;
            }

            itemsSentByMail += SendItemsByMail(insurance, mapId, sessionId);
        }
    }

    /// <summary>
    /// Populate the rest of the insurance package, then send it
    /// </summary>
    private int SendItemsByMail(Insurance insurance, string mapId, MongoId sessionId)
    {
        if (insurance.Items is null || insurance.Items.Count == 0)
        {
            return 0;
        }

        // Create a new root parent ID for the message we'll be sending the player
        var mailRootItemParentId = new MongoId();

        var traderBase = traderHelper.GetTrader(insurance.TraderId, sessionId);
        var maxInsuranceStorageTime = insuranceConfig.StorageTimeOverrideSeconds > 0d
            ? insuranceConfig.StorageTimeOverrideSeconds
            : timeUtil.GetHoursAsSeconds((int)traderBase!.Insurance!.MaxStorageTime!);
        var systemData = new SystemData
        {
            Date = timeUtil.GetBsgDateMailFormat(), Time = timeUtil.GetBsgTimeMailFormat(), Location = mapId,
        };
        var dialogueTemplates = tradersTable.GetTrader(insurance.TraderId)!.Dialogue;

        insurance.MaxStorageTime = (int)maxInsuranceStorageTime;
        insurance.SystemData = systemData;
        insurance.MessageType = MessageType.InsuranceReturn;
        insurance.MessageTemplateId = randomUtil.GetArrayValue(dialogueTemplates["insuranceFound"]!);
        insurance.Items = insurance.Items.AdoptOrphanedItems(mailRootItemParentId);

        insuranceController.SendMail(sessionId, insurance);
        return insurance.Items.Count;
    }

    private bool ShouldKeepAmmo(Item item)
    {
        return !config.LoseAmmoInMagazines
               && itemHelper.IsOfBaseclass(item.Template, BaseClasses.AMMO)
               && MagazineSlotIds.Contains(item.SlotId ?? string.Empty);
    }

    /// <summary>
    /// Check if insurance is allowed for the current map.
    /// Fallbacks to <c>false</c> if the map's insurance setting is not found
    /// </summary>
    private bool IsLocationInsuranceDisabled(string mapId)
    {
        return !(locationTable.GetLocation(mapId)?.Base.Insurance ?? true);
    }

    private void RemoveItemsFromInventory(PmcData pmcData, HashSet<MongoId> itemsToDelete)
    {
        if (pmcData.Inventory is null)
        {
            L.Error("Unexpected missing PmcData.Inventory");
            return;
        }
        if (pmcData.Inventory.Items is null)
        {
            L.Error("Unexpected missing pmcData.Inventory.Items");
            return;
        }

        pmcData.Inventory.Items = [.. pmcData.Inventory.Items.Where(i => !itemsToDelete.Contains(i.Id))];

        // Also remove from insured items list
        if (pmcData.InsuredItems is not null)
        {
            pmcData.InsuredItems = [.. pmcData.InsuredItems.Where(i => !itemsToDelete.Contains(i.ItemId.GetValueOrDefault()))];
        }
    }

    private void RemoveInsuranceFromItems(PmcData pmcData, List<Item> insuredItems)
    {
        if (pmcData.InsuredItems is null)
        {
            L.Error("Unexpected missing InsuredItems from PmcData");
            return;
        }

        var insuredItemsIds = insuredItems.Select(i => i.Id).ToHashSet();
        pmcData.InsuredItems = [.. pmcData.InsuredItems.Where(insuredItem => !insuredItemsIds.Contains(insuredItem.ItemId.GetValueOrDefault()))];
    }

    private void LogAfterProcessing(
        string? nickname,
        string mapId,
        int processedCount,
        int insuredCount,
        int remainedCount,
        int sentCount,
        int deletedCount
    )
    {
        L.Info($"""
                Insurance Report
                Player: {nickname}
                Map: {mapId}
                -
                Processed: {processedCount}
                Insured: {insuredCount}
                Not-Insured: {processedCount - insuredCount}
                -
                Remained: {remainedCount}
                Sent by Mail: {sentCount}
                Removed: {deletedCount}
                """);
    }

    public static HashSet<Item> GetAllItemsLostOnDeath(InRaidHelper inRaidHelper, PmcData pmcData)
    {
        var itemsLost = inRaidHelper.GetInventoryItemsLostOnDeath(pmcData);
        return [.. itemsLost.SelectMany(i => pmcData.Inventory?.Items?.GetItemWithChildren(i.Id) ?? Enumerable.Empty<Item>())];
    }

    /// <summary>
    /// Return the player's insured item from <seealso cref="BotBase.InsuredItems"/>
    /// </summary>
    /// <param name="data">PmcData</param>
    /// <param name="lostItemId">Insured item id to look for</param>
    /// <returns>The insured item</returns>
    public static InsuredItem? GetInsuredItem(BotBase data, MongoId lostItemId)
    {
        return data.InsuredItems?.FirstOrDefault(insuredItem => insuredItem.ItemId == lostItemId);
    }

    /// <summary>
    /// Modified <see cref="ItemHelper.GetEquipmentParent"/><br></br><br></br>
    /// This gives a HashSet of all the equipment's parents.
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
    /// <returns>A HashSet of parents item ids</returns>
    public static HashSet<MongoId> GetItemParentsIds(MongoId itemId, Dictionary<MongoId, Item> itemsMap)
    {
        var parentResults = new HashSet<MongoId>();
        var currentItem = itemsMap.GetValueOrDefault(itemId);

        while (currentItem is not null && !EquipmentSlotsAsStrings.Contains(currentItem.SlotId ?? string.Empty))
        {
            currentItem = itemsMap.GetValueOrDefault(currentItem.ParentId ?? string.Empty);
            if (currentItem is null)
            {
                break;
            }

            parentResults.Add(currentItem.Id);
        }

        return parentResults;
    }

    public static readonly FrozenSet<string> MagazineSlotIds =
        ["cartridges", "patron_in_weapon", "patron_in_weapon_000", "patron_in_weapon_001"];

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

    // TODO: if wipeOnRaidStart == true, runs twice! next run doesn't have anymore insurance for items on raid start...
}
