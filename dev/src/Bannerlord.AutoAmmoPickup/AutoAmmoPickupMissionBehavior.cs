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

                // Build the set of ammo we are currently interested in (refill existing or equip new stacks)
                if (!TryGetDesiredAmmoClasses(player, settings.PickupMode, out HashSet<WeaponClass> allowedRefillClasses, out HashSet<ItemObject.ItemTypeEnum> pickableAmmoTypes))
                    return;

                if (allowedRefillClasses.Count == 0 && pickableAmmoTypes.Count == 0)
                    return; // Player has no ranged weapons or all ammo stacks are full and no free slots

                // Use the configurable distance from MCM
                float pickupDistance = Math.Max(1.0f, Math.Min(6.0f, settings.AutoPickupDistance));

                SpawnedItemEntity nearest = FindNearestUsableAmmo(player, allowedRefillClasses, pickableAmmoTypes, pickupDistance);
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
        /// Populates two sets based on the current PickupMode from MCM:
        /// - allowedRefillClasses: WeaponClasses of ammo types we can immediately add to existing equipped stacks
        /// - pickableAmmoTypes: ItemTypes we can pick up into a free weapon slot
        ///
        /// In "OnlyEquippedWeaponAmmo" mode we restrict collection to the weapon the player is currently wielding.
        /// </summary>
        private bool TryGetDesiredAmmoClasses(
            Agent player,
            AutoPickupMode mode,
            out HashSet<WeaponClass> allowedRefillClasses,
            out HashSet<ItemObject.ItemTypeEnum> pickableAmmoTypes)
        {
            allowedRefillClasses = new HashSet<WeaponClass>();
            pickableAmmoTypes = new HashSet<ItemObject.ItemTypeEnum>();

            MissionEquipment equipment = player.Equipment;
            if (equipment == null)
                return false;

            bool hasEmptySlot = false;

            for (EquipmentIndex i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumPrimaryWeaponSlots; i++)
            {
                MissionWeapon mw = equipment[i];

                if (mw.IsEmpty)
                {
                    hasEmptySlot = true;
                    continue;
                }

                ItemObject item = mw.Item;
                if (item == null)
                    continue;

                if (item.Type == ItemObject.ItemTypeEnum.Bow)
                {
                    pickableAmmoTypes.Add(ItemObject.ItemTypeEnum.Arrows);
                }
                else if (item.Type == ItemObject.ItemTypeEnum.Crossbow)
                {
                    pickableAmmoTypes.Add(ItemObject.ItemTypeEnum.Bolts);
                }

                if (mw.IsAnyConsumable())
                {
                    if (mw.Amount < mw.MaxAmmo)
                    {
                        if (mw.CurrentUsageItem != null)
                        {
                            allowedRefillClasses.Add(mw.CurrentUsageItem.WeaponClass);
                        }
                    }

                    if (mw.Amount == 0)
                    {
                        hasEmptySlot = true;
                    }
                }
            }

            if (hasEmptySlot)
            {
                pickableAmmoTypes.Add(ItemObject.ItemTypeEnum.Thrown);
            }

            // === Strict mode: only the weapon currently in the player's hand ===
            if (mode == AutoPickupMode.OnlyEquippedWeaponAmmo)
            {
                allowedRefillClasses.Clear();
                pickableAmmoTypes.Clear();

                MissionWeapon wielded = player.WieldedWeapon;
                if (!wielded.IsEmpty && wielded.Item != null)
                {
                    var wieldedType = wielded.Item.Type;

                    if (wieldedType == ItemObject.ItemTypeEnum.Bow)
                    {
                        pickableAmmoTypes.Add(ItemObject.ItemTypeEnum.Arrows);
                    }
                    else if (wieldedType == ItemObject.ItemTypeEnum.Crossbow)
                    {
                        pickableAmmoTypes.Add(ItemObject.ItemTypeEnum.Bolts);
                    }
                    else if (wieldedType == ItemObject.ItemTypeEnum.Thrown)
                    {
                        pickableAmmoTypes.Add(ItemObject.ItemTypeEnum.Thrown);
                    }

                    if (wielded.IsAnyConsumable() && wielded.CurrentUsageItem != null)
                    {
                        if (wielded.Amount < wielded.MaxAmmo)
                        {
                            allowedRefillClasses.Add(wielded.CurrentUsageItem.WeaponClass);
                        }
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
            HashSet<ItemObject.ItemTypeEnum> pickableAmmoTypes,
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

                    bool canRefill = allowedRefillClasses.Contains(ammoClass);
                    bool canEquipNew = pickableAmmoTypes.Contains(itemType);

                    if (!canRefill && !canEquipNew)
                        continue;

                    // For new stacks (Arrows/Bolts/Thrown when no current stack of that type), the reference
                    // mod sometimes calls CanQuickPickUp. For auto we are lenient but still respect CanUseObject.
                    // If you want stricter behavior, uncomment:
                    // if (!canRefill && !player.CanQuickPickUp(dropped)) continue;

                    if (horizDistSq < bestDistSq)
                    {
                        bestDistSq = horizDistSq;
                        best = dropped;
                    }
                }
            }

            return best;
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
                var crouchModeProp = typeof(Agent).GetProperty("CrouchMode", BindingFlags.Public | BindingFlags.Instance);
                if (crouchModeProp != null)
                {
                    object modeObj = crouchModeProp.GetValue(agent);
                    if (modeObj != null)
                    {
                        string modeName = modeObj.ToString();
                        // "Crouched" is the value we care about (enum name or underlying value)
                        if (modeName == "Crouched" || modeName.EndsWith(".Crouched") || modeName == "1")
                            return true;
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
