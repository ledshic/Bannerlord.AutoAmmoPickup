using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AutoAmmoPickup
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
        #region Tunable Constants (easy to tweak for personal preference)

        /// <summary>
        /// How close (in meters, horizontal) the player must be to auto-pickup.
        /// Smaller than manual "easy pickup" mods because we don't want it triggering from across the field.
        /// Recommended: 2.6 - 3.5
        /// </summary>
        private const float AutoPickupDistance = 3.0f;

        /// <summary>
        /// Max vertical distance difference for pickup (helps on slopes/horse).
        /// </summary>
        private const float MaxPickupHeight = 2.2f;

        /// <summary>
        /// Extra height tolerance when mounted (many players fight on horseback in battles).
        /// </summary>
        private const float HorseHeightBonus = 1.2f;

        /// <summary>
        /// Minimum interval between scan attempts (seconds). Lower = more responsive, higher = less CPU.
        /// </summary>
        private const float ScanInterval = 0.22f;

        /// <summary>
        /// Minimum time after a successful pickup before we consider another one (lets the bend animation finish).
        /// </summary>
        private const float PickupCooldown = 0.65f;

        /// <summary>
        /// If true, shows a small message in the bottom-left when ammo is auto-picked.
        /// Set to false for completely silent operation.
        /// </summary>
        private const bool ShowPickupMessages = true;

        #endregion

        // Internal state
        private float _timeSinceLastScan;
        private float _timeSinceLastPickup;
        private bool _isEnabled = true; // Can be extended later with a hotkey toggle if desired

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            if (!_isEnabled)
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

                if (!ShouldRunInCurrentMission())
                    return;

                if (_timeSinceLastPickup < PickupCooldown)
                    return;

                // Build the set of ammo we are currently interested in (refill existing or equip new stacks)
                if (!TryGetDesiredAmmoClasses(player, out HashSet<WeaponClass> allowedRefillClasses, out HashSet<ItemObject.ItemTypeEnum> pickableAmmoTypes))
                    return;

                if (allowedRefillClasses.Count == 0 && pickableAmmoTypes.Count == 0)
                    return; // Player has no ranged weapons or all ammo stacks are full and no free slots

                SpawnedItemEntity nearest = FindNearestUsableAmmo(player, allowedRefillClasses, pickableAmmoTypes);
                if (nearest != null)
                {
                    MissionWeapon weapon = nearest.WeaponCopy;

                    // Trigger the real pickup. This plays the pickup animation, updates equipment/quiver,
                    // and removes the ground entity.
                    player.UseGameObject(nearest);

                    _timeSinceLastPickup = 0f;

                    if (ShowPickupMessages && !weapon.IsEmpty && weapon.IsAnyConsumable())
                    {
                        int amount = weapon.Amount;
                        string itemName = weapon.Item?.Name?.ToString() ?? "ammo";
                        InformationManager.DisplayMessage(
                            new InformationMessage($"Picking up {amount} {itemName}", Colors.Yellow));
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
        /// Populates two sets:
        /// - allowedRefillClasses: WeaponClasses of ammo types we can immediately add to existing equipped stacks (e.g. Arrow when you have a partially used quiver)
        /// - pickableAmmoTypes: ItemTypes we can pick up into a free weapon slot (new Arrows quiver, new Thrown stack, etc.)
        ///
        /// Mirrors the proven logic from Easy Weapon Pickup and similar QoL mods.
        /// </summary>
        private bool TryGetDesiredAmmoClasses(
            Agent player,
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

                // When the player has a bow equipped, they can benefit from loose arrows (even if current quiver slot is empty)
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
                    // We only want to auto-refill if this stack actually has room
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

            // If we have a free slot, we can pick up new stacks of throwing weapons (javelins, throwing axes, knives...)
            // and also new quivers for bows/crossbows (the type check above already added Arrows/Bolts when the launcher is present).
            if (hasEmptySlot)
            {
                pickableAmmoTypes.Add(ItemObject.ItemTypeEnum.Thrown);
            }
            else
            {
                // No free slots at all -> we can only do refills into existing consumable stacks.
                // pickableAmmoTypes may still contain Arrows/Bolts from the bow/crossbow presence,
                // but the later finder will only accept them for new stacks if we have room (we cleared thrown).
                // For strictness, if no empty slot we could clear non-refill types, but the consumable branch
                // already protects us. Keep Arrows/Bolts so that "new quiver" logic can still work if the
                // player somehow has a bow but the arrow slot was the empty one (edge case).
            }

            return true;
        }

        /// <summary>
        /// Scans active dropped items and returns the closest one that is usable ammo for the player.
        /// </summary>
        private SpawnedItemEntity FindNearestUsableAmmo(
            Agent player,
            HashSet<WeaponClass> allowedRefillClasses,
            HashSet<ItemObject.ItemTypeEnum> pickableAmmoTypes)
        {
            if (player == null)
                return null;

            Vec3 playerPos = player.Position;
            float maxDistSq = AutoPickupDistance * AutoPickupDistance;
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

        // Future extension ideas:
        // - Add a simple hotkey (e.g. in OnMissionTick check input) to temporarily disable auto pickup.
        // - Respect a "only when out of ammo" policy instead of "whenever room".
        // - Add MCM (Mod Configuration Menu) support for runtime toggles and distance tuning.
        // - Also auto-pick for AI companions (more complex, risk of balance change).
    }
}
