using InstantInsurance.Configuration;
using SPTarkov.Server.Core.Helpers.InRaid;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Ws;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Services.Server;

namespace InstantInsurance.Tests.Tests;

[TestFixture]
public class ControllerTests
{
    private static readonly MongoId _sessionId = new("68e7b24be3c1c89970af7294");

    private InstantInsuranceConfig _config;
    private InstantInsuranceController _controller;
    private InRaidHelper _inRaidHelper;
    private ItemHelper _itemHelper;
    private InsuranceConfig _insuranceConfig;
    private SaveServer _saveServer;
    private NotificationService _notificationService;
    private SPTarkov.Server.Core.Utils.Cloners.FastCloner _cloner;
    private ProfileActivityService _profileActivityService;
    private CancellationTokenSource? _tcs;

    private PmcData _originalPmcData;
    private PmcData _modifiedPmcData;
    private List<WsNotificationEvent> _notifications;

    [OneTimeSetUp]
    public async Task Initialize()
    {
        _tcs = new CancellationTokenSource();

        _config = DI.Get<InstantInsuranceConfig>();
        _controller = DI.Get<InstantInsuranceController>();
        _inRaidHelper = DI.Get<InRaidHelper>();
        _itemHelper = DI.Get<ItemHelper>();
        _insuranceConfig = DI.Get<InsuranceConfig>();
        _saveServer = DI.Get<SaveServer>();
        _notificationService = DI.Get<NotificationService>();
        _profileActivityService = DI.Get<ProfileActivityService>();
        _cloner = new SPTarkov.Server.Core.Utils.Cloners.FastCloner();

        // Load fresh dev profile
        await _saveServer.LoadProfileAsync(_sessionId, _tcs?.Token ?? CancellationToken.None);
        var profile = _saveServer.GetProfile(_sessionId);
        profile.ProfileInfo?.InvalidOrUnloadableProfile = true; // Disable saving
        _originalPmcData = profile.CharacterData.PmcData;
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        _tcs?.Cancel();
        _tcs?.Dispose();
        _tcs = null;
    }

    [SetUp]
    public void Prepare()
    {
        _modifiedPmcData = _cloner.Clone(_originalPmcData);

        // Ensure no existing queue messages
        _notifications = _notificationService.Get(_sessionId);
        _notifications.Clear();

        // Set location
        SetLocation("factory4_day");
    }

    [Test]
    public void NonTest_Simulate()
    {
        SetInsuranceReturnPercentChance(65, 0, 0);
        SetConfigValues();

        _controller.ProcessInventory(_modifiedPmcData, _sessionId);
    }

    /// <summary>
    /// Lose everything
    /// </summary>
    [Test]
    public void SimulateItemsBeingTaken_Enabled()
    {
        // Set 0 percent chance to be deterministic
        SetInsuranceReturnPercentChance(0, 0, 0);
        SetConfigValues(simulateItemsBeingTaken: true);

        _controller.ProcessInventory(_modifiedPmcData, _sessionId);

        var actualRemainingItems = GetAllItemsLostOnDeath(_modifiedPmcData).Where(IsNotAmmo);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(actualRemainingItems, Is.Empty, "All items should've been removed"); // Insured or non-insured
            Assert.That(_notifications, Is.Empty, "No items should have been sent by mail");
        }
    }

    /// <summary>
    /// Only non-insured items should be lost
    /// </summary>
    [Test]
    public void SimulateItemsBeingTaken_Disabled()
    {
        SetConfigValues(simulateItemsBeingTaken: false);

        _controller.ProcessInventory(_modifiedPmcData, _sessionId);

        var itemsToLose = GetAllItemsLostOnDeath(_originalPmcData);
        var expectedItemsToRemain = GetAllInsuredItems(_originalPmcData, itemsToLose);

        var actualRemainingItems = GetAllItemsLostOnDeath(_modifiedPmcData).Where(IsNotAmmo); // Remove ammo from equation
        var actualRemainingNonInsuredItems =
            GetAllNonInsuredItems(_originalPmcData, actualRemainingItems); // Use original to check if they're insured

        using (Assert.EnterMultipleScope())
        {
            Assert.That(actualRemainingNonInsuredItems, Is.Empty, "All non insured items should've been removed");
            Assert.That(
                expectedItemsToRemain.Select(i => i.Id),
                Is.EquivalentTo(actualRemainingItems.Select(i => i.Id)),
                "Insured items should remain on character"
            );
            Assert.That(_notifications, Is.Empty, "No items should have been sent by mail");
        }
    }

    [Test]
    public void LoseInsuranceOnItemsAfterDeath_Enabled()
    {
        SetConfigValues(loseInsuranceOnItemAfterDeath: true, simulateItemsBeingTaken: false);

        _controller.ProcessInventory(_modifiedPmcData, _sessionId);

        var itemsToLose = GetAllItemsLostOnDeath(_originalPmcData);
        var previouslyInsuredItems = GetAllInsuredItems(_originalPmcData, itemsToLose);

        // Find previously insured items in modified pmc data insurance list
        var actualInsuredItems = GetAllInsuredItems(_modifiedPmcData, previouslyInsuredItems);

        Assert.That(actualInsuredItems, Is.Empty, "All previously insured items should now be uninsured");
    }

    [Test]
    public void LoseInsuranceOnItemsAfterDeath_Disabled()
    {
        SetConfigValues(loseInsuranceOnItemAfterDeath: false, simulateItemsBeingTaken: false);

        _controller.ProcessInventory(_modifiedPmcData, _sessionId);

        var itemsToLose = GetAllItemsLostOnDeath(_originalPmcData);
        var expectedInsuredItems = GetAllInsuredItems(_originalPmcData, itemsToLose);

        // Find previously insured items in modified pmc data insurance list
        var actualInsuredItems = GetAllInsuredItems(_modifiedPmcData, expectedInsuredItems);

        Assert.That(
            expectedInsuredItems.Select(i => i.Id),
            Is.EquivalentTo(actualInsuredItems.Select(i => i.Id)),
            "All previously insured items should stay insured"
        );
    }

    [Test]
    public void LoseAmmoInMagazines_Enabled()
    {
        SetConfigValues(loseAmmoInMagazines: true, simulateItemsBeingTaken: false);

        _controller.ProcessInventory(_modifiedPmcData, _sessionId);

        var remainingAmmoItems = GetAllItemsLostOnDeath(_modifiedPmcData).Where(IsAmmo);

        Assert.That(remainingAmmoItems, Is.Empty, "All ammo items should've been removed");
    }

    [Test]
    public void LoseAmmoInMagazines_Disabled()
    {
        SetConfigValues(loseAmmoInMagazines: false, simulateItemsBeingTaken: false);

        _controller.ProcessInventory(_modifiedPmcData, _sessionId);

        var ammoInMagazinesToRemain = GetAllItemsLostOnDeath(_originalPmcData)
           .Where(IsAmmo)
           .Where(IsInMagazine)
           .Where(i => IsParentInsured(_originalPmcData, i));

        var remainingAmmoItems = GetAllItemsLostOnDeath(_modifiedPmcData).Where(IsAmmo);
        var ammoInMagazines = remainingAmmoItems.Where(IsInMagazine);
        var looseAmmo = remainingAmmoItems.Where(i => !IsInMagazine(i));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ammoInMagazines, Is.Not.Empty, "There should be at least some ammo in magazines remaining");
            Assert.That(looseAmmo, Is.Empty, "All loose ammo items should've been removed");
            Assert.That(
                ammoInMagazinesToRemain.Select(i => i.Id),
                Is.EquivalentTo(ammoInMagazines.Select(i => i.Id)),
                "All ammo in magazines should've remained"
            );
        }
    }

    [TestCase("laboratory")] // Assumed default config (disabled insurance)
    [TestCase("labyrinth")]
    public void DisabledLocations(string location)
    {
        // LoseInsuranceOnItemAfterDeath is overriden by location config's insurance for map
        SetLocation(location);
        SetConfigValues(simulateItemsBeingTaken: false, loseInsuranceOnItemAfterDeath: false, loseAmmoInMagazines: false);

        _controller.ProcessInventory(_modifiedPmcData, _sessionId);

        var itemsToLose = GetAllItemsLostOnDeath(_originalPmcData);
        var loseInsuranceToItems = GetAllInsuredItems(_originalPmcData, itemsToLose);

        var remainingItems = GetAllItemsLostOnDeath(_modifiedPmcData);
        var remainingInsuredItems = GetAllInsuredItems(_modifiedPmcData, loseInsuranceToItems);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_notifications, Is.Not.Empty, "Mail stating insurance failed for disabled map should have been sent");
            Assert.That(remainingItems, Is.Empty, "All items should have been removed for disabled map");
            Assert.That(remainingInsuredItems, Is.Empty, "All items should have been uninsured for disabled map");
        }
    }

    private void SetConfigValues(
        bool simulateItemsBeingTaken = true,
        bool loseInsuranceOnItemAfterDeath = true,
        bool loseAmmoInMagazines = false
    )
    {
        _config.SimulateItemsBeingTaken = simulateItemsBeingTaken;
        _config.LoseInsuranceOnItemAfterDeath = loseInsuranceOnItemAfterDeath;
        _config.LoseAmmoInMagazines = loseAmmoInMagazines;
    }

    private void SetInsuranceReturnPercentChance(double chance, double attachmentChance, double minAttachmentPrice)
    {
        foreach (var (traderId, _) in _insuranceConfig.ReturnChancePercent)
        {
            _insuranceConfig.ReturnChancePercent[traderId] = chance;
        }

        _insuranceConfig.ChanceNoAttachmentsTakenPercent = attachmentChance;
        _insuranceConfig.MinAttachmentRoublePriceToBeTaken = minAttachmentPrice;
    }

    private void SetLocation(string location)
    {
        var profileActivity = _profileActivityService.GetProfileActivityRaidData(_sessionId);
        profileActivity.RaidConfiguration ??= new GetRaidConfigurationRequestData();
        profileActivity.RaidConfiguration.Location = location;
    }

    /// <summary>
    /// Get all items to lose on death
    /// </summary>
    private IEnumerable<Item> GetAllItemsLostOnDeath(PmcData pmcData)
    {
        return InstantInsuranceController.GetAllItemsLostOnDeath(_inRaidHelper, pmcData);
    }

    private bool IsAmmo(Item item)
    {
        return _itemHelper.IsOfBaseclass(item.Template, BaseClasses.AMMO);
    }

    private bool IsNotAmmo(Item item)
    {
        return !IsAmmo(item);
    }

    /// <summary>
    /// Get insured items from a set of items
    /// </summary>
    private static IEnumerable<Item> GetAllInsuredItems(PmcData pmcData, IEnumerable<Item> items)
    {
        return items.Where(i => InstantInsuranceController.GetInsuredItem(pmcData, i.Id) != null);
    }

    /// <summary>
    /// Get non-insured items from a set of items
    /// </summary>
    private static IEnumerable<Item> GetAllNonInsuredItems(PmcData pmcData, IEnumerable<Item> items)
    {
        return items.Where(i => InstantInsuranceController.GetInsuredItem(pmcData, i.Id) == null);
    }

    private static bool IsInMagazine(Item item)
    {
        return InstantInsuranceController.MagazineSlotIds.Contains(item.SlotId ?? string.Empty);
    }

    private static bool IsParentInsured(BotBase data, Item item)
    {
        return InstantInsuranceController.GetInsuredItem(data, item.ParentId ?? MongoId.Empty()) != null;
    }
}
