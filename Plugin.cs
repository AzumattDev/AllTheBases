using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using UnityEngine;

namespace AllTheBases;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class AzuCoveredTheBasesPlugin : BaseUnityPlugin

{
    internal const string ModName = "AllTheBases";
    internal const string ModVersion = "1.0.8";
    internal const string Author = "Azumatt";
    private const string ModGUID = Author + "." + ModName;
    private static string ConfigFileName = ModGUID + ".cfg";
    private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
    internal static string ConnectionError = "";
    private readonly Harmony _harmony = new(ModGUID);
    public static readonly ManualLogSource AzuCoveredTheBasesLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
    private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

    public enum Toggle
    {
        Off,
        On
    }

    private void Awake()
    {
        _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
        _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);

        // Bases
        BaseHealth = config("2 - Bases", "Base Health", 25.0f, "The base amount of health the player has.");
        BaseStamina = config("2 - Bases", "Base Stamina", 50.0f, "The base amount of stamina the player has.");
        BaseEitr = config("2 - Bases", "Base Eitr", 0.0f, "The base amount of Eitr the player has.");
        BaseSneakSpeed = config("2 - Bases", "Base Sneak Speed", 2.0f, "The base sneak speed of the player.");
        BaseCarryWeight = config("2 - Bases", "Base Carry Weight", 300.0f, "The base amount of carry weight the player has.");
        Basemegingjord = config("2 - Bases", "Base Megingjord Boost", 150.0f, "The amount of carry weight the Megingjord gives");
        BaseUnarmedDamage = config("2 - Bases", "Base Unarmed Damage", 70.0f, "The base unarmed damage multiplied by your skill level. 120 will result in a maximum of up to 12 damage when you have a skill level of 10.");

        // Eitr Alterations
        EitrIsEnabled = config("3 - Eitr Alterations", "Eitr alterations enabled", Toggle.Off, new ConfigDescription("Eitr alterations enabled" + Environment.NewLine + "Note: These are not percent drains. They are direct drain values.", null, new ConfigurationManagerAttributes() { Order = 1 }));
        EitrRegen = config("3 - Eitr Alterations", "Eitr Regen", 5f, "The amount of Eitr the player regenerates per second.");
        EitrRegenDelay = config("3 - Eitr Alterations", "Eitr Regen Delay", 1f, "The amount of time in seconds before Eitr regeneration starts.");

        // Stamina Alterations
        StaminaIsEnabled = config("4 - Stamina Alterations", "Stamina alterations enabled", Toggle.Off, new ConfigDescription("Stamina alterations enabled" + Environment.NewLine + "Note: These are not percent drains. They are direct drain values.", null, new ConfigurationManagerAttributes() { Order = 1 }));

        DodgeStaminaUsage = config("4 - Stamina Alterations", "Dodge Stamina Usage", 10f, "Dodge Stamina Usage");
        EncumberedStaminaDrain = config("4 - Stamina Alterations", "Encumbered Stamina drain", 10f, "Encumbered Stamina drain");
        SneakStaminaDrain = config("4 - Stamina Alterations", "Sneak Stamina Drain", 5f, "Sneak stamina drain");
        RunStaminaDrain = config("4 - Stamina Alterations", "Run Stamina Drain", 10f, "Run Stamina Drain");
        StaminaRegenDelay = config("4 - Stamina Alterations", "Delay before stamina regeneration starts", 1f, "Delay before stamina regeneration starts");
        StaminaRegen = config("4 - Stamina Alterations", "Stamina regen factor", 5f, "Stamina regen factor");
        SwimStaminaDrain = config("4 - Stamina Alterations", "Stamina drain from swim", 5f, "Stamina drain from swim");
        JumpStaminaDrain = config("4 - Stamina Alterations", "Jump stamina drain factor", 10f, "Stamina drain factor for jumping");
        DisableCameraShake = config<float>("5 - Additional", "Cam shake factor", 0, "Cam Shake factor", false);
        MaximumPlacementDistance = config("5 - Additional", "Build distance alteration", 5f, "Build Distance  (Maximum Placement Distance)");
        MaximumInteractDistance = config("5 - Additional", "Interact distance alteration", 5f, "Interact Distance (Maximum Interact Distance)");
        ShowDamageFlash = config("5 - Additional", "ShowDamageFlash", Toggle.On, "Show the flashing red screen when taking damage");
        BaseAutoPickUpRange = config("5 - Additional", "Auto pickup range adjustment", 2f, "Auto pickup range adjustment");

        // Delegates for all config options
        BaseEitr.SettingChanged += BasesChanged;
        BaseCarryWeight.SettingChanged += BasesChanged;
        Basemegingjord.SettingChanged += BasesChanged;
        EitrIsEnabled.SettingChanged += BasesChanged;
        EitrRegen.SettingChanged += BasesChanged;
        EitrRegenDelay.SettingChanged += BasesChanged;
        StaminaIsEnabled.SettingChanged += BasesChanged;
        DodgeStaminaUsage.SettingChanged += BasesChanged;
        EncumberedStaminaDrain.SettingChanged += BasesChanged;
        SneakStaminaDrain.SettingChanged += BasesChanged;
        RunStaminaDrain.SettingChanged += BasesChanged;
        StaminaRegenDelay.SettingChanged += BasesChanged;
        StaminaRegen.SettingChanged += BasesChanged;
        SwimStaminaDrain.SettingChanged += BasesChanged;
        JumpStaminaDrain.SettingChanged += BasesChanged;
        DisableCameraShake.SettingChanged += BasesChanged;
        MaximumPlacementDistance.SettingChanged += BasesChanged;
        ShowDamageFlash.SettingChanged += BasesChanged;
        BaseAutoPickUpRange.SettingChanged += BasesChanged;

        AutoDoc();
        _harmony.PatchAll();
        SetupWatcher();
    }


    private void OnDestroy()
    {
        Config.Save();
    }

    private void SetupWatcher()
    {
        FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
        watcher.Changed += ReadConfigValues;
        watcher.Created += ReadConfigValues;
        watcher.Renamed += ReadConfigValues;
        watcher.IncludeSubdirectories = true;
        watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
        watcher.EnableRaisingEvents = true;
    }

    private void ReadConfigValues(object sender, FileSystemEventArgs e)
    {
        if (!File.Exists(ConfigFileFullPath)) return;
        try
        {
            AzuCoveredTheBasesLogger.LogDebug("ReadConfigValues called");
            Config.Reload();
        }
        catch
        {
            AzuCoveredTheBasesLogger.LogError($"There was an issue loading your {ConfigFileName}");
            AzuCoveredTheBasesLogger.LogError("Please check your config entries for spelling and format!");
        }
    }

    private void BasesChanged(object sender, EventArgs e)
    {
        try
        {
            if (Player.m_localPlayer != null)
                UpdateBases(ref Player.m_localPlayer);
        }
        catch
        {
            AzuCoveredTheBasesLogger.LogError($"There was an issue updating the base configurations live. This might require a restart instead.");
        }
    }

    internal void AutoDoc()
    {
#if DEBUG
        // Store Regex to get all characters after a [
        Regex regex = new(@"\[(.*?)\]");

        // Strip using the regex above from Config[x].Description.Description
        string Strip(string x) => regex.Match(x).Groups[1].Value;
        StringBuilder sb = new();
        string lastSection = "";
        foreach (ConfigDefinition x in Config.Keys)
        {
            // skip first line
            if (x.Section != lastSection)
            {
                lastSection = x.Section;
                sb.Append($"{Environment.NewLine}`{x.Section}`{Environment.NewLine}");
            }

            sb.Append($"\n{x.Key} [{Strip(Config[x].Description.Description)}]" +
                      $"{Environment.NewLine}   * {Config[x].Description.Description.Replace("[Synced with Server]", "").Replace("[Not Synced with Server]", "")}" +
                      $"{Environment.NewLine}     * Default Value: {Config[x].GetSerializedValue()}{Environment.NewLine}");
        }

        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, $"{ModName}_AutoDoc.md"), sb.ToString());
#endif
    }


    #region ConfigOptions

    private static ConfigEntry<Toggle> _serverConfigLocked = null!;
    public static ConfigEntry<float> BaseHealth = null!;
    internal static ConfigEntry<float> BaseStamina = null!;
    internal static ConfigEntry<float> BaseEitr = null!;
    internal static ConfigEntry<float> BaseSneakSpeed = null!;
    public static ConfigEntry<float> BaseCarryWeight = null!;
    internal static ConfigEntry<float> Basemegingjord = null!;
    internal static ConfigEntry<float> BaseUnarmedDamage = null!;

    public static ConfigEntry<Toggle> EitrIsEnabled = null!;
    public static ConfigEntry<float> EitrRegenDelay = null!;
    public static ConfigEntry<float> EitrRegen = null!;

    public static ConfigEntry<Toggle> StaminaIsEnabled = null!;

    public static ConfigEntry<float> DodgeStaminaUsage = null!;
    public static ConfigEntry<float> EncumberedStaminaDrain = null!;
    public static ConfigEntry<float> SneakStaminaDrain = null!;
    public static ConfigEntry<float> RunStaminaDrain = null!;
    public static ConfigEntry<float> StaminaRegenDelay = null!;
    public static ConfigEntry<float> StaminaRegen = null!;
    public static ConfigEntry<float> SwimStaminaDrain = null!;
    public static ConfigEntry<float> JumpStaminaDrain = null!;
    public static ConfigEntry<float> BaseAutoPickUpRange = null!;
    public static ConfigEntry<float> DisableCameraShake = null!;
    public static ConfigEntry<float> MaximumPlacementDistance = null!;
    public static ConfigEntry<float> MaximumInteractDistance = null!;
    public static ConfigEntry<Toggle> ShowDamageFlash = null!;

    private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description,
        bool synchronizedSetting = true)
    {
        ConfigDescription extendedDescription = new(description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"), description.AcceptableValues, description.Tags);
        ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
        //var configEntry = Config.Bind(group, name, value, description);

        SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private ConfigEntry<T> config<T>(string group, string name, T value, string description,
        bool synchronizedSetting = true)
    {
        return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
    }

    private class ConfigurationManagerAttributes
    {
        [UsedImplicitly] public int? Order;
        [UsedImplicitly] public bool? Browsable;
        [UsedImplicitly] public string? Category;
        [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer;
    }

    class AcceptableShortcuts : AcceptableValueBase // Used for KeyboardShortcut Configs 
    {
        public AcceptableShortcuts() : base(typeof(KeyboardShortcut))
        {
        }

        public override object Clamp(object value) => value;
        public override bool IsValid(object value) => true;

        public override string ToDescriptionString() =>
            "# Acceptable values: " + string.Join(", ", UnityInput.Current.SupportedKeyCodes);
    }

    #endregion

    internal static void UpdateBases(ref Player player)
    {
        if (StaminaIsEnabled.Value == Toggle.On)
        {
            player.m_dodgeStaminaUsage = DodgeStaminaUsage.Value;
            player.m_encumberedStaminaDrain = EncumberedStaminaDrain.Value;
            player.m_sneakStaminaDrain = SneakStaminaDrain.Value;
            player.m_runStaminaDrain = RunStaminaDrain.Value;
            player.m_staminaRegenDelay = StaminaRegenDelay.Value;
            player.m_staminaRegen = StaminaRegen.Value;
            player.m_swimStaminaDrainMinSkill = SwimStaminaDrain.Value;
            player.m_swimStaminaDrainMaxSkill = SwimStaminaDrain.Value;
            player.m_jumpStaminaUsage = JumpStaminaDrain.Value;
        }

        if (EitrIsEnabled.Value == Toggle.On)
        {
            try
            {
                float eitr;
                player.GetTotalFoodValue(out float _, out float _, out eitr);
                player.SetMaxEitr(eitr, true);
            }
            catch
            {
                // ignored
            }

            player.m_eiterRegen = EitrRegen.Value;
            player.m_eitrRegenDelay = EitrRegenDelay.Value;
        }

        player.m_autoPickupRange = BaseAutoPickUpRange.Value;
        player.m_baseCameraShake = DisableCameraShake.Value;
        player.m_maxPlaceDistance = MaximumPlacementDistance.Value;
        player.m_maxInteractDistance = MaximumInteractDistance.Value;
        player.m_maxCarryWeight = BaseCarryWeight.Value;
        player.m_baseStamina = BaseStamina.Value;
        if (!(player.m_stamina < player.m_baseStamina)) return;
        player.m_stamina = player.m_baseStamina;
        PlayerAwakePatch.AlteredSuccessfully = true;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.Awake))]
public static class PlayerAwakePatch
{
    public static bool AlteredSuccessfully = false;

    public static void Postfix(ref Player __instance)
    {
        AzuCoveredTheBasesPlugin.UpdateBases(ref __instance);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.GetBaseFoodHP))]
static class PlayerGetBaseFoodHpPatch
{
    static void Prefix(Player __instance)
    {
        __instance.m_baseStamina = AzuCoveredTheBasesPlugin.BaseStamina.Value;
        if (PlayerAwakePatch.AlteredSuccessfully) return;
        __instance.m_stamina = __instance.m_baseStamina;
        PlayerAwakePatch.AlteredSuccessfully = true;
    }
}

/*[HarmonyPriority(Priority.High)]
[HarmonyPatch(typeof(Player), nameof(Player.GetBaseFoodHP))]
static class IncreaseBaseHealth
{
    [UsedImplicitly]
    private static void Postfix(Player __instance, ref float __result)
    {
       var currentResult = __result;
       AzuCoveredTheBasesPlugin.AzuCoveredTheBasesLogger.LogInfo($"Current Base Health: {currentResult}");
       __result += AzuCoveredTheBasesPlugin.BaseHealth.Value - currentResult;
       AzuCoveredTheBasesPlugin.AzuCoveredTheBasesLogger.LogInfo($"New Base Health: {__result}");
    }
}*/

[HarmonyPatch(typeof(Player), nameof(Player.GetTotalFoodValue))]
static class PlayerGetTotalFoodValuePatch
{
    [UsedImplicitly]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        //FieldInfo baseHpField = AccessTools.DeclaredField(typeof(Player), nameof(Player.m_baseHP));
        foreach (CodeInstruction instruction in instructions)
        {
            /*if (instruction.opcode == OpCodes.Ldfld && instruction.OperandIs(baseHpField))
            {
                yield return new CodeInstruction(OpCodes.Call,
                    AccessTools.DeclaredMethod(typeof(Player), nameof(Player.GetBaseFoodHP)));
            }*/
            if (instruction.opcode == OpCodes.Ldc_R4)
            {
                yield return new CodeInstruction(OpCodes.Call,
                    AccessTools.DeclaredMethod(typeof(PlayerGetTotalFoodValuePatch), nameof(ChangeBaseEitr)));
            }
            else
            {
                yield return instruction;
            }
        }
    }

    public static void Postfix(ref float hp)
    {
        float hpaddition = (AzuCoveredTheBasesPlugin.BaseHealth.Value - 25.0f);
        if (hpaddition > 0)
        {
            hp += hpaddition;
        }
    }

    private static float ChangeBaseEitr()
    {
        return AzuCoveredTheBasesPlugin.BaseEitr.Value;
    }
}

[HarmonyPatch(typeof(SE_Stats), nameof(SE_Stats.Setup))]
static class SeStatsSetupPatch
{
    static void Postfix(ref SE_Stats __instance)
    {
        if (__instance.m_addMaxCarryWeight > 0)
            __instance.m_addMaxCarryWeight = (__instance.m_addMaxCarryWeight - 150f) +
                                             AzuCoveredTheBasesPlugin.Basemegingjord.Value;
    }
}

[HarmonyPatch(typeof(Hud), nameof(Hud.DamageFlash))]
public static class HudDamageFlashPatch
{
    private static void Postfix(Hud __instance)
    {
        __instance.m_damageScreen.gameObject.SetActive(AzuCoveredTheBasesPlugin.ShowDamageFlash.Value == AzuCoveredTheBasesPlugin.Toggle.On);
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetCurrentWeapon))]
static class HumanoidGetCurrentWeaponPatch
{
    private static ItemDrop.ItemData Postfix(ItemDrop.ItemData __weapon, ref Character __instance)
    {
        if (__weapon == null) return __weapon;
        if (__weapon.m_shared.m_name != "Unarmed") return __weapon;
        if (__instance is not Player CharacterPlayerInstance) return __weapon;
        __weapon.m_shared.m_damages.m_blunt = CharacterPlayerInstance.GetSkillFactor(Skills.SkillType.Unarmed) * AzuCoveredTheBasesPlugin.BaseUnarmedDamage.Value;
        if (__weapon.m_shared.m_damages.m_blunt <= 2)
            __weapon.m_shared.m_damages.m_blunt = 2;
        return __weapon;
    }
}

[HarmonyPatch(typeof(Terminal), nameof(Terminal.TryRunCommand))]
static class Terminal_TryRunCommand_Patch
{
    static void Postfix(Terminal __instance, string text, bool silentFail = false, bool skipAllowedCheck = false)
    {
        if (text.ToLower().Contains("skill"))
        {
            AzuCoveredTheBasesPlugin.UpdateBases(ref Player.m_localPlayer);
        }
    }
}