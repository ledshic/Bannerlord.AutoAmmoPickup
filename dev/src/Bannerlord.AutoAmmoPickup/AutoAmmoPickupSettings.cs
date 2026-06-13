using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using System;
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

        private bool _modeDefault = true;
        private bool _modeOnlyEquippedWeaponAmmo;
        private bool _modeDisabled;

        [SettingPropertyBool(
            "{=AAM_ModeOptionDefault}Mode: Default - any usable ammo from equipped weapons",
            RequireRestart = false,
            HintText = "{=AAM_ModeDefaultHint}Picks up any usable ammo type inferred from your equipped weapons and free slots.")]
        [SettingPropertyGroup("{=AAM_General}General")]
        public bool ModeDefault
        {
            get => _modeDefault;
            set => SetExclusiveMode(AutoPickupMode.Default, value);
        }

        [SettingPropertyBool(
            "{=AAM_ModeOptionEquipped}Mode: Only currently equipped ranged weapon ammo",
            RequireRestart = false,
            HintText = "{=AAM_ModeEquippedHint}Restricts pickup to ammo compatible with the currently wielded ranged weapon.")]
        [SettingPropertyGroup("{=AAM_General}General")]
        public bool ModeOnlyEquippedWeaponAmmo
        {
            get => _modeOnlyEquippedWeaponAmmo;
            set => SetExclusiveMode(AutoPickupMode.OnlyEquippedWeaponAmmo, value);
        }

        [SettingPropertyBool(
            "{=AAM_ModeOptionDisabled}Mode: Disabled",
            RequireRestart = false,
            HintText = "{=AAM_ModeDisabledHint}Disables automatic ammo pickup.")]
        [SettingPropertyGroup("{=AAM_General}General")]
        public bool ModeDisabled
        {
            get => _modeDisabled;
            set => SetExclusiveMode(AutoPickupMode.Disabled, value);
        }

        /// <summary>
        /// Effective pickup mode consumed by mission behavior.
        /// </summary>
        public AutoPickupMode PickupMode
        {
            get
            {
                if (_modeDisabled)
                    return AutoPickupMode.Disabled;

                if (_modeOnlyEquippedWeaponAmmo)
                    return AutoPickupMode.OnlyEquippedWeaponAmmo;

                return AutoPickupMode.Default;
            }
        }

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

        private void SetExclusiveMode(AutoPickupMode requestedMode, bool selected)
        {
            if (!selected)
            {
                // Keep at least one mode selected; this emulates a radio group with checkboxes.
                OnPropertyChanged(nameof(ModeDefault));
                OnPropertyChanged(nameof(ModeOnlyEquippedWeaponAmmo));
                OnPropertyChanged(nameof(ModeDisabled));
                return;
            }

            bool newDefault = requestedMode == AutoPickupMode.Default;
            bool newOnlyEquipped = requestedMode == AutoPickupMode.OnlyEquippedWeaponAmmo;
            bool newDisabled = requestedMode == AutoPickupMode.Disabled;

            if (_modeDefault == newDefault &&
                _modeOnlyEquippedWeaponAmmo == newOnlyEquipped &&
                _modeDisabled == newDisabled)
            {
                return;
            }

            _modeDefault = newDefault;
            _modeOnlyEquippedWeaponAmmo = newOnlyEquipped;
            _modeDisabled = newDisabled;

            OnPropertyChanged(nameof(ModeDefault));
            OnPropertyChanged(nameof(ModeOnlyEquippedWeaponAmmo));
            OnPropertyChanged(nameof(ModeDisabled));
        }

        #endregion
    }
}
