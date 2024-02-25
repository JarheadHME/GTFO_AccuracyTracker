using Agents;
using CellMenu;
using Gear;
using Hikaria.AccuracyTracker.Handlers;
using Hikaria.AccuracyTracker.Managers;
using Player;
using SNetwork;
using TheArchive.Core.Attributes;
using TheArchive.Core.Attributes.Feature.Settings;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Core.Models;
using TheArchive.Loader;
using UnityEngine;

namespace Hikaria.AccuracyTracker.Features;

[EnableFeatureByDefault]
[DisallowInGameToggle]
public class AccuracyTracker : Feature
{
    public override string Name => "Accuracy Tracker";

    public override bool InlineSettingsIntoParentMenu => true;

    #region FeatureSettings
    [FeatureConfig]
    public static AccuracyTrackerSettings Settings { get; set; }

    public class AccuracyTrackerSettings
    {
        [FSDisplayName("Enabled")]
        public bool Enabled { get => AccuracyUpdater.Enabled; set => AccuracyUpdater.Enabled = value; }

        [FSDisplayName("Show Other Players' Accuracy")]
        public bool ShowOtherPlayersAcc { get => AccuracyUpdater.ShowOtherPlayersAcc; set => AccuracyUpdater.ShowOtherPlayersAcc = value; }

        [FSDisplayName("Show Bots' Accuracy")]
        public bool ShowBotsAcc { get => AccuracyUpdater.ShowBotsAcc; set => AccuracyUpdater.ShowBotsAcc = value; }

        [FSDisplayName("Display Format")]
        [FSDescription("{0}: Player Name\n{1}: Hitrate\n{2}: Weakpoint Hitrate\n{3}: Weapoint Hits\n{4}: Hits Landed\n{5}: Shots Fired")]
        public string ShowFormat { get => AccuracyUpdater.ShowFormat; set => AccuracyUpdater.ShowFormat = value; }

        [FSDisplayName("Success Screen Display Format")]
        [FSDescription("{0}: Hitrate\n{1}: Weakpoint Hitrate\n{2}: Weapoint Hits\n{3}: Hits Landed\n{4}: Shots Fired")]
        public string EndscreenFormat { get => AccuracyUpdater.EndscreenFormat; set => AccuracyUpdater.EndscreenFormat = value; }

        [FSDisplayName("Show Colors on Endscreen")]
        public bool ShowColorsOnEndscreen { get; set; } = false;

        [FSHeader("Player Name Settings")]
        [FSDisplayName("Use Generic Player Name")]
        [FSDescription("Uses Player's Nickname if disabled")]
        public bool UseGenericName { get => AccuracyUpdater.UseGenericName; set => AccuracyUpdater.UseGenericName = value; }

        [FSInline]
        [FSHeader("Display Location Settings")]
        [FSDisplayName("Display Location Settings")]
        public PositionSettings Position { get; set; } = new();

        [FSInline]
        [FSHeader("Display Location Settings")]
        [FSDisplayName("Display Color Settings")]
        public ColorSettings FontColors { get; set; } = new();
    }

    public class PlayerNameEntry
    {
        public PlayerNameEntry(string character, string name)
        {
            Character = character;
            Name = name;
        }

        [FSSeparator]
        [FSDisplayName("Character")]
        [FSReadOnly]
        public string Character { get; set; }
        [FSDisplayName("Generic Name")]
        public string Name
        {
            get
            {
                return _name;
            }
            set
            {
                if (AccuracyUpdater.IsSetup)
                {
                    AccuracyUpdater.MarkAllAccuracyDataNeedUpdate();
                }
                _name = value;
            }
        }

        private string _name;
    }

    public class PositionSettings
    {
        [FSDisplayName("X Offset")]
        [FSDescription("In Pixels")]
        public int OffsetX
        {
            get
            {
                return AccuracyUpdater.OffsetX;
            }
            set
            {
                AccuracyUpdater.OffsetX = value;
            }
        }

        [FSDisplayName("Y Offset")]
        [FSDescription("In Pixels")]
        public int OffsetY
        {
            get
            {
                return AccuracyUpdater.OffsetY;
            }
            set
            {
                AccuracyUpdater.OffsetY = value;
            }
        }
    }

    public class ColorSettings
    {
        [FSDisplayName("Hit Rate Color")]
        public SColor HittedRatioColor { get; set; } = new(0.7206f, 0.7206f, 0.7206f, 0.3137f);
        [FSDisplayName("Hits Landed Color")]
        public SColor HittedColor { get; set; } = new(0.7206f, 0.7206f, 0.7206f, 0.3137f);
        [FSDisplayName("Weak Point Hitrate Color")]
        public SColor WeakspotHittedRatioColor { get; set; } = new(0.7206f, 0.7206f, 0.7206f, 0.3137f);
        [FSDisplayName("Weak Point Hits Landed Color")]
        public SColor WeakspotHittedColor { get; set; } = new(0.7206f, 0.7206f, 0.7206f, 0.3137f);
        [FSDisplayName("Shots Fired Color")]
        public SColor ShottedColor { get; set; } = new(0.7206f, 0.7206f, 0.7206f, 0.3137f);
    }

    #endregion

    #region FeatureMethods
    public override void OnGameStateChanged(int state)
    {
        if (state == (int)eGameStateName.Lobby)
        {
            AccuracyUpdater.DoClear();
        }
    }

    public override void Init()
    {
        LoaderWrapper.ClassInjector.RegisterTypeInIl2Cpp<AccuracyUpdater>();
        AccuracyManager.Setup();
    }
    #endregion

    #region SetupAccuracyShower
    public static bool IsSetup { get; private set; }

    [ArchivePatch(typeof(CM_PageRundown_New), nameof(CM_PageRundown_New.Setup))]
    public class CM_PageRundown_New__Setup__Postfix
    {
        private static void Postfix()
        {
            if (!IsSetup)
            {
                GameObject gameObject = new("AccurayShower");
                UnityEngine.Object.DontDestroyOnLoad(gameObject);
                if (gameObject.GetComponent<AccuracyUpdater>() == null)
                {
                    gameObject.AddComponent<AccuracyUpdater>();
                }
                IsSetup = true;
            }
        }
    }

    [ArchivePatch(typeof(CM_PageExpeditionSuccess), nameof(CM_PageExpeditionSuccess.TryGetArchetypeName))]
    private class CM_PageExpeditionSuccess__TryGetArchetypeName__Patch
    {
        private static void Postfix(PlayerBackpack backpack, InventorySlot slot, ref string name)
        {
            if (slot != InventorySlot.GearStandard && slot != InventorySlot.GearSpecial)
                return;
            if (AccuracyUpdater.TryGetPlayerAccuracyData(backpack.Owner, out var data))
            {
                name += $" | {data.GetAccuracyText(slot)}";
            }
        }
    }
    #endregion

    #region HookOnSessionMemberChanged
    [ArchivePatch(typeof(SNet_GlobalManager), nameof(SNet_GlobalManager.Setup))]
    private class SNet_GlobalManager__Setup__Patch
    {
        private static void Postfix()
        {
            SNet_Events.OnMasterChanged += new Action(OnMasterChanged);
            SNet_Events.OnPlayerEvent += new Action<SNet_Player, SNet_PlayerEvent, SNet_PlayerEventReason>(OnPlayerEvent);
        }
    }

    private static void OnPlayerEvent(SNet_Player player, SNet_PlayerEvent playerEvent, SNet_PlayerEventReason reason)
    {
        AccuracyManager.BroadcastAccuracyDataListener();
        switch (playerEvent)
        {
            case SNet_PlayerEvent.PlayerLeftSessionHub:
            case SNet_PlayerEvent.PlayerAgentDeSpawned:
                AccuracyManager.OnSessionMemberChanged(player, SessionMemberEvent.LeftSessionHub);
                break;
            case SNet_PlayerEvent.PlayerAgentSpawned:
                AccuracyManager.OnSessionMemberChanged(player, SessionMemberEvent.JoinSessionHub);
                break;
        }
    }

    private static void OnMasterChanged()
    {
        AccuracyUpdater.CheckAndSetVisible();
    }

    public enum SessionMemberEvent
    {
        JoinSessionHub,
        LeftSessionHub
    }
    #endregion

    #region FetchSentryFire

    // 只有主机才需要获取炮台是否开火
    public static bool IsSentryGunFire { get; private set; }

    [ArchivePatch(typeof(SentryGunInstance_Firing_Bullets), nameof(SentryGunInstance_Firing_Bullets.FireBullet))]
    private class SentryGunInstance_Firing_Bullets__FireBullet__Patch
    {
        private static void Prefix()
        {
            IsSentryGunFire = true;
        }

        private static void Postfix()
        {
            IsSentryGunFire = false;
        }
    }

    [ArchivePatch(typeof(SentryGunInstance_Firing_Bullets), nameof(SentryGunInstance_Firing_Bullets.UpdateFireShotgunSemi))]
    private class SentryGunInstance_Firing_Bullets__UpdateFireShotgunSemi__Patch
    {
        private static void Prefix()
        {
            IsSentryGunFire = true;
        }

        private static void Postfix()
        {
            IsSentryGunFire = false;
        }
    }

    #endregion

    #region FetchOtherPlayersFire
    private static Dictionary<ulong, InventorySlot> LastWieldValidSlot { get; set; } = new();

    [ArchivePatch(typeof(PlayerSync), nameof(PlayerSync.SyncInventoryStatus))]
    private class PlayerSync__SyncInventoryStatus__Patch
    {
        private static void Prefix(PlayerSync __instance)
        {
            var player = __instance.m_agent.Owner;
            if (player.IsLocal || !SNet.IsMaster || player.IsBot)
            {
                return;
            }
            if (AccuracyManager.IsAccuracyListener(player.Lookup))
            {
                return;
            }
            var slot = __instance.GetWieldedSlot();
            if (slot == InventorySlot.GearStandard || slot == InventorySlot.GearSpecial)
            {
                LastWieldValidSlot[player.Lookup] = slot;
            }
        }
    }

    // 用于解决延迟问题导致的开火次数错误，非完美解决方法，针对主机时其他玩家
    [ArchivePatch(typeof(PlayerInventorySynced), nameof(PlayerInventorySynced.GetSync))]
    private class PlayerInventorySynced__GetSync__Patch
    {
        private static void Prefix(PlayerInventorySynced __instance)
        {
            if (__instance.Owner == null)
            {
                return;
            }
            var player = __instance.Owner.Owner;
            if (!SNet.IsMaster || player.IsBot || player.IsLocal)
            {
                return;
            }
            if (AccuracyManager.IsAccuracyListener(player.Lookup))
            {
                return;
            }
            var wieldSlot = __instance.Owner.Inventory.WieldedSlot;
            if (wieldSlot == InventorySlot.GearClass)
            {
                return;
            }
            uint count = (uint)__instance.Owner.Sync.FireCountSync;
            if (__instance.WieldedItem != null && (wieldSlot == InventorySlot.GearStandard || wieldSlot == InventorySlot.GearSpecial))
            {
                var bulletWeapon = __instance.WieldedItem.TryCast<BulletWeaponSynced>();
                if (bulletWeapon != null)
                {
                    var shotGun = bulletWeapon.TryCast<ShotgunSynced>();
                    if (shotGun != null && shotGun.ArchetypeData != null)
                    {
                        count *= (uint)shotGun.ArchetypeData.ShotgunBulletCount;
                    }
                }
            }
            else if (LastWieldValidSlot.TryGetValue(player.Lookup, out wieldSlot))
            {
                if (!PlayerBackpackManager.TryGetBackpack(player, out var backpack) || !backpack.TryGetBackpackItem(wieldSlot, out var backpackItem))
                {
                    return;
                }
                var bulletWeapon = backpackItem.Instance.TryCast<BulletWeaponSynced>();
                if (bulletWeapon != null)
                {
                    var shotGun = bulletWeapon.TryCast<ShotgunSynced>();
                    if (shotGun != null && shotGun.ArchetypeData != null)
                    {
                        count *= (uint)shotGun.ArchetypeData.ShotgunBulletCount;
                    }
                }
                else
                {
                    Logs.LogError("Not BulletWeapon but fire bullets?");
                }
            }
            AccuracyUpdater.AddShotted(player.Lookup, wieldSlot, count);
            AccuracyUpdater.MarkAccuracyDataNeedUpdate(player.Lookup);
        }
    }

    // 做主机时使用该方法
    [ArchivePatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ReceiveBulletDamage))]
    private class Dam_EnemyDamageBase__ReceiveBulletDamage__Patch
    {
        private static void Postfix(Dam_EnemyDamageBase __instance, pBulletDamageData data)
        {
            if (IsSentryGunFire)
                return;
            if (data.source.TryGet(out var agent))
            {
                var playerAgent = agent.TryCast<PlayerAgent>();
                if (playerAgent == null)
                {
                    return;
                }
                var player = playerAgent.Owner;
                if (AccuracyManager.IsAccuracyListener(player.Lookup) || player.IsLocal || player.IsBot)
                {
                    return;
                }
                var slot = playerAgent.Inventory.WieldedSlot;
                if (slot != InventorySlot.GearStandard && slot != InventorySlot.GearSpecial)
                {
                    Logs.LogError("Not wielding BulletWeapon but ReceiveBulletDamage?");
                    return;
                }
                if (__instance.DamageLimbs[data.limbID].m_type == eLimbDamageType.Weakspot)
                {
                    AccuracyUpdater.AddWeakspotHitted(player.Lookup, slot, 1);
                }
                AccuracyUpdater.AddHitted(player.Lookup, slot, 1);
                AccuracyUpdater.MarkAccuracyDataNeedUpdate(player.Lookup);
            }
        }
    }
    #endregion

    #region FetchBotsFireWhenHost
    private static bool IsWeaponOwner(BulletWeapon weapon)
    {
        if (weapon == null || weapon.Owner == null || weapon.Owner.Owner == null)
        {
            return false;
        }
        return weapon.Owner.Owner.IsLocal;
    }

    private static uint HitCount;

    private static int BulletPiercingLimit;

    private static int BulletsCountPerFire;

    private static int BulletHitCalledCount;

    private static bool IsPiercingBullet;

    private static bool IsInWeaponFire;

    private static bool CanCalc;

    [ArchivePatch(typeof(BulletWeaponSynced), nameof(BulletWeaponSynced.Fire))]
    private class BulletWeaponSynced__Fire__Patch
    {
        private static void Prefix(BulletWeaponSynced __instance)
        {
            if (!__instance.Owner.Owner.IsBot || !SNet.IsMaster)
            {
                return;
            }
            CanCalc = true;
            IsInWeaponFire = true;
            IsPiercingBullet = false;
            BulletPiercingLimit = 1;
            HitCount = 0;
            BulletHitCalledCount = 0;
            BulletsCountPerFire = 1;
            var data = __instance.ArchetypeData;
            if (data != null)
            {
                IsPiercingBullet = data.PiercingBullets;
                BulletPiercingLimit = data.PiercingBullets ? data.PiercingDamageCountLimit : 0;
            }
        }
        private static void Postfix(BulletWeapon __instance)
        {
            if (!__instance.Owner.Owner.IsBot || !SNet.IsMaster)
            {
                return;
            }
            CanCalc = false;
            IsInWeaponFire = false;
            var player = __instance.Owner.Owner;
            AccuracyUpdater.AddShotted(player.Lookup, __instance.ItemDataBlock.inventorySlot, (uint)BulletsCountPerFire);
            AccuracyUpdater.AddHitted(player.Lookup, __instance.ItemDataBlock.inventorySlot, HitCount);
            AccuracyUpdater.MarkAccuracyDataNeedUpdate(player.Lookup);
        }
    }

    [ArchivePatch(typeof(ShotgunSynced), nameof(ShotgunSynced.Fire))]
    private class ShotgunSynced__Fire__Patch
    {
        private static void Prefix(Shotgun __instance)
        {
            if (!__instance.Owner.Owner.IsBot || !SNet.IsMaster)
            {
                return;
            }
            CanCalc = true;
            IsInWeaponFire = true;
            IsPiercingBullet = false;
            BulletPiercingLimit = 1;
            HitCount = 0;
            BulletHitCalledCount = 0;
            BulletsCountPerFire = 1;
            var data = __instance.ArchetypeData;
            if (data != null)
            {
                IsPiercingBullet = data.PiercingBullets;
                BulletPiercingLimit = data.PiercingBullets ? data.PiercingDamageCountLimit : 0;
                BulletsCountPerFire = data.ShotgunBulletCount;
            }
        }
        private static void Postfix(BulletWeapon __instance)
        {
            if (!__instance.Owner.Owner.IsBot || !SNet.IsMaster)
            {
                return;
            }
            CanCalc = false;
            IsInWeaponFire = false;
            var player = __instance.Owner.Owner;
            AccuracyUpdater.AddShotted(player.Lookup, __instance.ItemDataBlock.inventorySlot, (uint)BulletsCountPerFire);
            AccuracyUpdater.AddHitted(player.Lookup, __instance.ItemDataBlock.inventorySlot, HitCount);
            AccuracyUpdater.MarkAccuracyDataNeedUpdate(player.Lookup);
        }
    }
    #endregion

    #region FetchLocalFire

    [ArchivePatch(typeof(BulletWeapon), nameof(BulletWeapon.Fire))]
    private class BulletWeapon__Fire__Patch
    {
        private static void Prefix(BulletWeapon __instance)
        {
            if (!IsWeaponOwner(__instance))
            {
                return;
            }
            CanCalc = true;
            IsInWeaponFire = true;
            IsPiercingBullet = false;
            BulletPiercingLimit = 1;
            HitCount = 0;
            BulletHitCalledCount = 0;
            BulletsCountPerFire = 1;
            var data = __instance.ArchetypeData;
            if (data != null)
            {
                IsPiercingBullet = data.PiercingBullets;
                BulletPiercingLimit = data.PiercingBullets ? data.PiercingDamageCountLimit : 0;
            }
        }

        private static void Postfix(BulletWeapon __instance)
        {
            if (!IsWeaponOwner(__instance))
            {
                return;
            }
            CanCalc = false;
            IsInWeaponFire = false;
            var player = __instance.Owner.Owner;
            AccuracyUpdater.AddShotted(player.Lookup, __instance.ItemDataBlock.inventorySlot, (uint)BulletsCountPerFire);
            AccuracyUpdater.AddHitted(player.Lookup, __instance.ItemDataBlock.inventorySlot, HitCount);
            AccuracyUpdater.MarkAccuracyDataNeedUpdate(player.Lookup);
        }
    }

    [ArchivePatch(typeof(Shotgun), nameof(Shotgun.Fire))]
    private class Shotgun__Fire__Patch
    {
        private static void Prefix(Shotgun __instance)
        {
            if (!IsWeaponOwner(__instance))
            {
                return;
            }
            CanCalc = true;
            IsInWeaponFire = true;
            IsPiercingBullet = false;
            BulletPiercingLimit = 1;
            HitCount = 0;
            BulletHitCalledCount = 0;
            BulletsCountPerFire = 1;
            var data = __instance.ArchetypeData;
            if (data != null)
            {
                IsPiercingBullet = data.PiercingBullets;
                BulletPiercingLimit = data.PiercingBullets ? data.PiercingDamageCountLimit : 0;
                BulletsCountPerFire = data.ShotgunBulletCount;
            }
        }

        private static void Postfix(Shotgun __instance)
        {
            if (!IsWeaponOwner(__instance))
            {
                return;
            }
            CanCalc = false;
            IsInWeaponFire = false;
            var player = __instance.Owner.Owner;
            AccuracyUpdater.AddShotted(player.Lookup, __instance.ItemDataBlock.inventorySlot, (uint)BulletsCountPerFire);
            AccuracyUpdater.AddHitted(player.Lookup, __instance.ItemDataBlock.inventorySlot, HitCount);
            AccuracyUpdater.MarkAccuracyDataNeedUpdate(player.Lookup);
        }
    }

    [ArchivePatch(typeof(Dam_EnemyDamageLimb), nameof(Dam_EnemyDamageLimb.BulletDamage))]
    private class Dam_EnemyDamageLimb__BulletDamage__Patch
    {
        private static void Postfix(Dam_EnemyDamageLimb __instance, Agent sourceAgent)
        {
            if (!CanCalc || sourceAgent == null)
            {
                return;
            }
            var playerAgent = sourceAgent.TryCast<PlayerAgent>();
            if (playerAgent != null && __instance.m_type == eLimbDamageType.Weakspot)
            {
                AccuracyUpdater.AddWeakspotHitted(playerAgent.Owner.Lookup, playerAgent.Inventory.WieldedSlot, 1);
            }
        }
    }

    #endregion

    #region HandleFire
    [ArchivePatch(typeof(BulletWeapon), nameof(BulletWeapon.BulletHit))]
    private class BulletWeapon__BulletHit__Patch
    {
        private static bool CanCalcHitted;
        private static void Postfix(bool __result)
        {
            if (!IsInWeaponFire || IsSentryGunFire || !CanCalc)
            {
                return;
            }
            if (BulletHitCalledCount >= BulletsCountPerFire)
            {
                return;
            }
            if (IsPiercingBullet)
            {
                if (BulletHitCalledCount % BulletPiercingLimit == 0)
                {
                    CanCalcHitted = true;
                }
            }
            else
            {
                CanCalcHitted = true;
            }
            BulletHitCalledCount++;
            if (__result && CanCalcHitted)
            {
                HitCount++;
                CanCalcHitted = false;
            }
        }
    }
    #endregion
}
