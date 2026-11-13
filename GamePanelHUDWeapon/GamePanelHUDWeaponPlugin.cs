#if !UNITY_EDITOR

using BepInEx;
using GamePanelHUDCore.Attributes;
using GamePanelHUDCore.Models;
using KmyTarkovUtils;
using SettingsModel = GamePanelHUDWeapon.Models.SettingsModel;

namespace GamePanelHUDWeapon
{
    [BepInPlugin("com.dustinmoon78.gamepanelhudweapon", "GamePanelHUDWeapon", "4.0.0")]
    [BepInDependency("com.dustinmoon78.gamepanelhudcore", "4.0.0")]
    [EFTConfigurationPluginAttributes("https://hub.sp-tarkov.com/files/file/652-game-panel-hud", @"localized\weapon")]
    public class GamePanelHUDWeaponPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            SettingsModel.Create(Config);
        }

        private void Start()
        {
            foreach (var value in HUDCoreModel.Instance.LoadHUD("gamepanelweaponhud.bundle", "GamePanelWeaponHUD")
                         .Init.Values)
            {
                value.ReplaceAllFont(EFTFontHelper.BenderNormal);
            }
        }
    }
}

#endif