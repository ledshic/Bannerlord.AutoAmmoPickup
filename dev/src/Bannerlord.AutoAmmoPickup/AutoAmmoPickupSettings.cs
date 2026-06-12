using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using System.ComponentModel;
using TaleWorlds.Localization;

namespace Bannerlord.AutoAmmoPickup
{
    /// <summary>
    /// Pickup behavior modes exposed in the MCM UI.
    /// </summary>
    public enum AutoPickupMode
    {
        [Description("{=AAM_ModeDefault}Default - Any usable ammo based on your equipped weapons")]
        Default = 0,

        [Description("{=AAM_ModeEquipped}Only ammo for the currently equipped ranged weapon (in-hand)")]
        OnlyEquippedWeaponAmmo = 1,

        [Description("{=AAM_ModeDisabled}Disabled")]
        Disabled = 2
    }

    /// <summary>
    /// MCM Global Settings for Bannerlord.AutoAmmoPickup.
    /// All options appear under "Auto Ammo Pickup" in Mod Options.
    /// </summary>
    public sealed class AutoAmmoPickupSettings : AttributeGlobalSettings<AutoAmmoPickupSettings>
    {
        public override string Id => "Bannerlord.AutoAmmoPickup_v1";
        public override string DisplayName
        {
            get
            {
                var ver = typeof(AutoAmmoPickupSettings).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
                return new TextObject("{=AAM_SettingsTitle}Auto Ammo Pickup {VERSION}", new System.Collections.Generic.Dictionary<string, object>
                {
                    { "VERSION", ver }
                }).ToString();
            }
        }
        public override string FolderName => "Bannerlord.AutoAmmoPickup";
        public override string FormatType => "json";

        #region General

        [SettingPropertyBool(
            "{=AAM_Enabled}Enable Mod",
            RequireRestart = false,
            HintText = "{=AAM_EnabledHint}Master toggle. When off, no automatic ammo pickup will occur.")]
        [SettingPropertyGroup("{=AAM_General}General", GroupOrder = 0)]
        public bool ModEnabled { get; set; } = true;

        [SettingPropertyDropdown(
            "{=AAM_PickupMode}Pickup Mode",
            RequireRestart = false,
            HintText = "{=AAM_PickupModeHint}Controls what kind of ammo the mod will automatically pick up.")]
        [SettingPropertyGroup("{=AAM_General}General")]
        public AutoPickupMode PickupMode { get; set; } = AutoPickupMode.Default;

        [SettingPropertyBool(
            "{=AAM_DisableCrouch}Disable while crouching",
            RequireRestart = false,
            HintText = "{=AAM_DisableCrouchHint}When checked (default), automatic pickup is suspended while your character is in the crouched stance (default toggle key: Z). Useful to avoid picking up ammo while trying to stay hidden or aim carefully.")]
        [SettingPropertyGroup("{=AAM_General}General")]
        public bool DisableWhileCrouching { get; set; } = true;

        [SettingPropertyBool(
            "{=AAM_ShowMessages}Show Pickup Messages",
            RequireRestart = false,
            HintText = "{=AAM_ShowMessagesHint}Display a small yellow message when ammo is automatically picked up.")]
        [SettingPropertyGroup("{=AAM_General}General")]
        public bool ShowPickupMessages { get; set; } = true;

        #endregion

        #region Tuning

        [SettingPropertyFloatingInteger(
            "{=AAM_Distance}Auto Pickup Distance (meters)",
            1.0f, 6.0f,
            "0.0",
            RequireRestart = false,
            HintText = "{=AAM_DistanceHint}How close (horizontal) the player must be to auto-pick up ammo. Lower values feel more realistic.")]
        [SettingPropertyGroup("{=AAM_Tuning}Tuning", GroupOrder = 1)]
        public float AutoPickupDistance { get; set; } = 3.0f;

        #endregion
    }
}
