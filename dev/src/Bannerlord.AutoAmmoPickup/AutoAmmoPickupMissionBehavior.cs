using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace Bannerlord.AutoAmmoPickup
{
    /// <summary>
    /// Mission behavior that automatically picks up nearby usable ammo for the player
    /// (arrows, bolts, throwing weapons, etc.) during battles, arena matches, and similar combat missions.
    ///
    /// It uses the same low-level APIs as popular "Easy Weapon Pickup" style mods:
    /// - Mission.GetActiveEntitiesWithScriptComponentOfType&lt;SpawnedItemEntity&gt;()
    /// - Agent.UseGameObject(...) to trigger the full vanilla pickup (animation + equipment + OnItemPickup hooks)
    /// </summary>
    public class AutoAmmoPickupMissionBehavior : MissionLogic
    {
        #region Internal Timing / Physics (not exposed in MCM for simplicity, but easy to promote later)

        private const float MaxPickupHeight = 2.2f;
        private const float HorseHeightBonus = 1.2f;
        private const float ScanInterval = 0.22f;
        private const float PickupCooldown = 0.65f;

        #endregion

        // Internal state
        private float _timeSinceLastScan;
        private float _timeSinceLastPickup;

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            var settings = AutoAmmoPickupSettings.Instance;
            if (settings == null || !settings.ModEnabled || settings.PickupMode == AutoPickupMode.Disabled)
                return;

            _timeSinceLastScan += dt;
            _timeSinceLastPickup += dt;

            // Throttle the expensive spatial query
            if (_timeSinceLastScan < ScanInterval)
                return;

            _timeSinceLastScan = 0f;

            try
            {
                Agent player = Agent.Main;
                if (player == null || !player.IsActive() || player.IsUsingGameObject)
                    return;

                // New configurable option: suspend autopick while crouching (default = true)
                // In Bannerlord the default crouch key is "Z" and it is a toggle (not a hold).
                // We query the actual agent crouch state using the real API at runtime.
                if (settings.DisableWhileCrouching && IsAgentCrouching(player))
                    return;

                if (!ShouldRunInCurrentMission())
                    return;

                if (_timeSinceLastPickup < PickupCooldown)
                    return;

                // Build refill filters from existing non-full ammo stacks only.
                // This allows subtype merge (e.g. regular arrows <-> piercing arrows)
                // while still forbidding new stack acquisition or loadout replacement.
                if (!TryGetDesiredAmmoClasses(
                    player,
                    settings.PickupMode,
                    out HashSet<WeaponClass> allowedRefillClasses,
                    out HashSet<ItemObject.ItemTypeEnum> allowedRefillItemTypes))
                    return;

                if (allowedRefillClasses.Count == 0 && allowedRefillItemTypes.Count == 0)
                    return; // No refillable ammo stacks: do not pick up anything

                // Use the configurable distance from MCM
                float pickupDistance = Math.Max(1.0f, Math.Min(6.0f, settings.AutoPickupDistance));

                SpawnedItemEntity nearest = FindNearestUsableAmmo(
                    player,
                    allowedRefillClasses,
                    allowedRefillItemTypes,
                    pickupDistance);
                if (nearest != null)
                {
                    MissionWeapon weapon = nearest.WeaponCopy;

                    // Trigger the real pickup. This plays the pickup animation, updates equipment/quiver,
                    // and removes the ground entity.
                    player.UseGameObject(nearest);

                    _timeSinceLastPickup = 0f;

                    if (settings.ShowPickupMessages && !weapon.IsEmpty && weapon.IsAnyConsumable())
                    {
                        int amount = weapon.Amount;
                        string itemName = weapon.Item?.Name?.ToString() ?? "ammo";
                        var pickupMsg = new TextObject("{=AAM_PICKUP}Picking up {AMOUNT} {ITEM}");
                        pickupMsg.SetTextVariable("AMOUNT", amount);
                        pickupMsg.SetTextVariable("ITEM", itemName);
                        InformationManager.DisplayMessage(
                            new InformationMessage(pickupMsg.ToString(), Colors.Yellow));
                    }
                }
            }
            catch (Exception ex)
            {
                // Fail silently in release to avoid spamming player. In development you can enable logging.
                // For debugging, uncomment:
                // InformationManager.DisplayMessage(new InformationMessage("AutoAmmoPickup error: " + ex.Message, Colors.Red));
                MBDebug.ShowWarning("AutoAmmoPickup tick error: " + ex);
            }
        }

        /// <summary>
        /// Returns true only in missions where the player is actually fighting and would want battlefield ammo.
        /// Covers: field battles, sieges, arena fights, custom battles, etc.
        /// </summary>
        private bool ShouldRunInCurrentMission()
        {
            Mission mission = Mission.Current;
            if (mission == null)
                return false;

            // Broad but practical filter for "battle or arena" scenarios
            if (mission.IsFieldBattle ||
                mission.IsSiegeBattle ||
                mission.IsSallyOutBattle)
            {
                return true;
            }

            // MissionMode catches Custom Battle, Duel, and many arena/practice fights
            if (mission.Mode == MissionMode.Battle ||
                mission.Mode == MissionMode.Duel)
            {
                return true;
            }

            // As a last resort, if the player has an active combat agent we allow it
            // (covers some training fields / special missions).
            return Agent.Main != null && Agent.Main.IsActive();
        }

        /// <summary>
        /// Populates refill filters from existing equipped ammo stacks that are not full.
        /// We track both WeaponClass and ItemType so different ammo subtypes can still refill each other.
        /// No mode is allowed to pick up new ammo/weapons into empty slots or by replacing current loadout.
        /// </summary>
        private bool TryGetDesiredAmmoClasses(
            Agent player,
            AutoPickupMode mode,
            out HashSet<WeaponClass> allowedRefillClasses,
            out HashSet<ItemObject.ItemTypeEnum> allowedRefillItemTypes)
        {
            allowedRefillClasses = new HashSet<WeaponClass>();
            allowedRefillItemTypes = new HashSet<ItemObject.ItemTypeEnum>();

            MissionEquipment equipment = player.Equipment;
            if (equipment == null)
                return false;

            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumPrimaryWeaponSlots; i++)
            {
                MissionWeapon mw = equipment[i];

                if (mw.IsEmpty)
                {
                    continue;
                }

                ItemObject item = mw.Item;
                if (item == null)
                    continue;

                if (mw.IsAnyConsumable())
                {
                    if (mw.Amount < mw.MaxAmmo)
                    {
                        if (mw.CurrentUsageItem != null)
                        {
                            allowedRefillClasses.Add(mw.CurrentUsageItem.WeaponClass);
                        }

                        if (IsRefillAmmoItemType(item.Type))
                        {
                            allowedRefillItemTypes.Add(item.Type);
                        }
                    }

                }
            }

            // === Strict mode: only the weapon currently in the player's hand ===
            if (mode == AutoPickupMode.OnlyEquippedWeaponAmmo)
            {
                allowedRefillClasses.Clear();
                allowedRefillItemTypes.Clear();

                MissionWeapon wielded = player.WieldedWeapon;
                if (TryGetWieldedAmmoItemType(wielded, out ItemObject.ItemTypeEnum wantedAmmoType))
                {
                    for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumPrimaryWeaponSlots; i++)
                    {
                        MissionWeapon mw = equipment[i];
                        if (mw.IsEmpty || !mw.IsAnyConsumable() || mw.Item == null)
                            continue;

                        if (mw.Amount >= mw.MaxAmmo)
                            continue;

                        if (mw.Item.Type != wantedAmmoType)
                            continue;

                        if (mw.CurrentUsageItem != null)
                        {
                            allowedRefillClasses.Add(mw.CurrentUsageItem.WeaponClass);
                        }

                        allowedRefillItemTypes.Add(mw.Item.Type);
                    }
                }
                // If nothing is wielded (e.g. holding a sword), strict mode picks up nothing.
            }

            return true;
        }

        /// <summary>
        /// Scans active dropped items and returns the closest one that is usable ammo for the player.
        /// </summary>
        private SpawnedItemEntity FindNearestUsableAmmo(
            Agent player,
            HashSet<WeaponClass> allowedRefillClasses,
            HashSet<ItemObject.ItemTypeEnum> allowedRefillItemTypes,
            float autoPickupDistance)
        {
            if (player == null)
                return null;

            Vec3 playerPos = player.Position;
            float maxDistSq = autoPickupDistance * autoPickupDistance;
            float heightTolerance = MaxPickupHeight + (player.MountAgent != null ? HorseHeightBonus : 0f);

            List<WeakGameEntity> entities = Mission.GetActiveEntitiesWithScriptComponentOfType<SpawnedItemEntity>().ToList();

            SpawnedItemEntity best = null;
            float bestDistSq = float.MaxValue;

            foreach (WeakGameEntity ge in entities)
            {
                if (ge == null)
                    continue;

                Vec3 pos = ge.GlobalPosition;
                float horizDistSq = pos.AsVec2.DistanceSquared(playerPos.AsVec2);
                if (horizDistSq > maxDistSq)
                    continue;

                float vertDiff = Math.Abs(pos.Z - playerPos.Z);
                if (vertDiff > heightTolerance)
                    continue;

                // Get the actual script component(s)
                foreach (SpawnedItemEntity dropped in ge.GetScriptComponents<SpawnedItemEntity>())
                {
                    if (dropped == null)
                        continue;

                    MissionWeapon w = dropped.WeaponCopy;
                    if (w.IsEmpty || dropped.IsDisabledForPlayers || !player.CanUseObject(dropped))
                        continue;

                    if (w.Amount <= 0)
                        continue;

                    bool isConsumable = w.IsAnyConsumable();
                    if (!isConsumable)
                        continue; // We only care about ammo resources, not dropped swords etc.

                    WeaponClass ammoClass = w.CurrentUsageItem != null ? w.CurrentUsageItem.WeaponClass : WeaponClass.Undefined;
                    ItemObject.ItemTypeEnum itemType = w.Item != null ? w.Item.Type : ItemObject.ItemTypeEnum.Invalid;

                    // Refill-only policy: never pick up items that would create a new stack or replace loadout.
                    bool canRefillByClass = allowedRefillClasses.Contains(ammoClass);
                    bool canRefillByType = allowedRefillItemTypes.Contains(itemType);
                    if (!canRefillByClass && !canRefillByType)
                        continue;

                    if (horizDistSq < bestDistSq)
                    {
                        bestDistSq = horizDistSq;
                        best = dropped;
                    }
                }
            }

            return best;
        }

        private static bool IsRefillAmmoItemType(ItemObject.ItemTypeEnum itemType)
        {
            return itemType == ItemObject.ItemTypeEnum.Arrows ||
                   itemType == ItemObject.ItemTypeEnum.Bolts ||
                   itemType == ItemObject.ItemTypeEnum.Thrown;
        }

        private static bool TryGetWieldedAmmoItemType(MissionWeapon wielded, out ItemObject.ItemTypeEnum ammoType)
        {
            ammoType = ItemObject.ItemTypeEnum.Invalid;

            if (wielded.IsEmpty || wielded.Item == null)
                return false;

            if (wielded.Item.Type == ItemObject.ItemTypeEnum.Bow)
            {
                ammoType = ItemObject.ItemTypeEnum.Arrows;
                return true;
            }

            if (wielded.Item.Type == ItemObject.ItemTypeEnum.Crossbow)
            {
                ammoType = ItemObject.ItemTypeEnum.Bolts;
                return true;
            }

            if (wielded.Item.Type == ItemObject.ItemTypeEnum.Thrown)
            {
                ammoType = ItemObject.ItemTypeEnum.Thrown;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if the given agent is currently in the crouched stance.
        ///
        /// Bannerlord's default crouch ("Z" key) is a toggle, not a hold.
        /// We use reflection + direct property access so it works both with limited
        /// ReferenceAssemblies at compile time and the real game assemblies at runtime.
        /// Preferred properties (in order): IsCrouching, then CrouchMode == Crouched.
        /// </summary>
        private static bool IsAgentCrouching(Agent agent)
        {
            if (agent == null || !agent.IsActive())
                return false;

            try
            {
                // 1. Direct IsCrouching property (exists in many versions of the game)
                var isCrouchingProp = typeof(Agent).GetProperty("IsCrouching", BindingFlags.Public | BindingFlags.Instance);
                if (isCrouchingProp != null)
                {
                    object val = isCrouchingProp.GetValue(agent);
                    if (val is bool b) return b;
                }

                // 2. CrouchMode enum (very common in current Bannerlord)
                // Try both "CrouchMode" and "crouchMode" property names to handle different versions
                PropertyInfo crouchModeProp = typeof(Agent).GetProperty("CrouchMode", BindingFlags.Public | BindingFlags.Instance)
                                           ?? typeof(Agent).GetProperty("crouchMode", BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Instance);
                if (crouchModeProp != null)
                {
                    object modeObj = crouchModeProp.GetValue(agent);
                    if (modeObj != null)
                    {
                        // Get the underlying integer value of the enum
                        int enumValue = Convert.ToInt32(modeObj);

                        // Check for common CrouchMode enum values:
                        // NotCrouching = 0, Crouching = 1, Crouched = 2 (or similar variants)
                        // We consider both "Crouching" (transitioning) and "Crouched" (fully crouched) as crouched
                        if (enumValue >= 1)
                            return true;

                        // Fallback: string-based comparison for unrecognized enum values
                        string modeName = modeObj.ToString();
                        if (!string.IsNullOrEmpty(modeName))
                        {
                            // Remove namespace qualification if present (e.g. "Agent+CrouchMode.Crouched" -> "Crouched")
                            modeName = modeName.Contains('.') ? modeName.Substring(modeName.LastIndexOf('.') + 1) : modeName;

                            if (modeName.Equals("Crouched", StringComparison.OrdinalIgnoreCase) ||
                                modeName.Equals("Crouching", StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                    }
                }
            }
            catch
            {
                // Swallow reflection errors; fall through to false
            }

            return false;
        }

        // Future extension ideas:
        // - Add a hotkey (e.g. Ctrl+something) to temporarily toggle auto pickup.
        // - "Only when low on ammo" policy.
        // - Auto-pick for companions (risk of balance / performance).
        // - More granular per-weapon-type toggles in MCM.
    }
}
