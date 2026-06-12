using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace Bannerlord.AutoAmmoPickup
{
    /// <summary>
    /// Entry point for the Auto Ammo Pickup module.
    /// Registers the mission behavior that handles automatic pickup of usable ammo (arrows, bolts, throwing weapons)
    /// when the player is close to them during battles, arena fights, etc.
    /// </summary>
    public class AutoAmmoPickupSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            // You can register custom state options or early hooks here if needed.
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

            // Friendly startup message so players know the mod is active.
            var loadedMsg = new TextObject("{=AAM_LOADED}Auto Ammo Pickup loaded. Ammo will be automatically collected in battles when in range.");
            InformationManager.DisplayMessage(
                new InformationMessage(loadedMsg.ToString(), Colors.Cyan));
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            // Campaign or custom battle game models can be added here if extending further.
        }

        /// <summary>
        /// This is called for every new mission (battlefield, arena, siege, custom battle, etc.).
        /// We add our behavior so it can scan for nearby ammo every tick.
        /// </summary>
        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);

            if (mission == null)
                return;

            // Add our auto-pickup behavior. It internally guards to only act for the player in combat situations.
            mission.AddMissionBehavior(new AutoAmmoPickupMissionBehavior());
        }
    }
}
