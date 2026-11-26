using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BezbroGames.Scamper.HandTracking;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR.Management;

namespace FlatRatScamper;

[BepInPlugin(Id, "FlatRat-Scamper", Version)]
public sealed class Plugin : BasePlugin
{
    private const string Id = "jaxon.flatrat.scamper";
    private const string Version = "0.1.5";
    private const float DefaultWalkSpeed = 2.35f;
    private const float DefaultSprintSpeed = 4f;
    private Harmony harmony;
    private FlatRatController controller;
    private static ConfigEntry<bool> desktopEnabled = null!;
    internal static ManualLogSource Logger = null!;
    internal static ConfigEntry<float> MouseSensitivity = null!;
    internal static ConfigEntry<float> StandingEyeHeight = null!;
    internal static ConfigEntry<float> WalkSpeed = null!;
    internal static ConfigEntry<float> SprintSpeed = null!;
    internal static ConfigEntry<float> JumpHeight = null!;

    public override void Load()
    {
        Logger = Log;
        bool migrateSpeedDefaults = HasOldMovementDefaults();
        desktopEnabled = Config.Bind("Desktop", "Enabled", true, "Enable desktop controls at launch. Set false and restart for VR.");
        if (!desktopEnabled.Value)
        {
            return;
        }

        MouseSensitivity = Config.Bind("Desktop", "MouseSensitivity", 0.13f, "Degrees of view rotation per mouse pixel.");
        StandingEyeHeight = Config.Bind("Desktop", "EyeHeight", 1.58f,
            "Standing tracking-space eye height in metres when no headset pose is available.");
        WalkSpeed = Config.Bind("Movement", "WalkSpeed", DefaultWalkSpeed, "Walking speed in metres per second at normal rig scale.");
        SprintSpeed = Config.Bind("Movement", "SprintSpeed", DefaultSprintSpeed, "Sprinting speed in metres per second at normal rig scale.");
        if (migrateSpeedDefaults && WalkSpeed.Value == 2.1f) WalkSpeed.Value = DefaultWalkSpeed;
        if (migrateSpeedDefaults && SprintSpeed.Value == 3.6f) SprintSpeed.Value = DefaultSprintSpeed;
        JumpHeight = Config.Bind("Movement", "JumpHeight", 0.55f, "Grounded jump height in metres at normal rig scale.");

        harmony = new Harmony(Id);
        try
        {
            Patch(typeof(XRGeneralSettings), "AttemptInitializeXRSDKOnLoad", nameof(SkipAutomaticXr));
            Patch(typeof(XRGeneralSettings), "AttemptStartXRSDKOnBeforeSplashScreen", nameof(SkipAutomaticXr));
            Patch(typeof(Player), nameof(Player.ResolveHandLocomotionRole), nameof(DesktopHandRole));
            Patch(typeof(Player), nameof(Player.FixedUpdate), nameof(BeforePlayerPhysics), nameof(AfterPlayerPhysics));
            controller = AddComponent<FlatRatController>();
            Log.LogInfo("FlatRat-Scamper loaded. Credit: J_axon.");
        }
        catch
        {
            harmony.UnpatchSelf();
            throw;
        }
    }

    private void Patch(Type type, string methodName, string prefix, string postfix = null)
    {
        var target = AccessTools.DeclaredMethod(type, methodName);
        if (target == null)
        {
            throw new MissingMethodException(type.FullName, methodName);
        }
        harmony!.Patch(target, prefix: new HarmonyMethod(typeof(Plugin), prefix),
            postfix: postfix == null ? null : new HarmonyMethod(typeof(Plugin), postfix));
    }

    public static bool SkipAutomaticXr() => !desktopEnabled.Value;

    private bool HasOldMovementDefaults()
    {
        const string header = "## Settings file was created by plugin FlatRat-Scamper v";
        try
        {
            if (!File.Exists(Config.ConfigFilePath)) return false;
            string firstLine = File.ReadLines(Config.ConfigFilePath).FirstOrDefault();
            return firstLine?.StartsWith(header, StringComparison.Ordinal) == true &&
                System.Version.TryParse(firstLine[header.Length..], out var previous) && previous < new System.Version(Version);
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static bool DesktopHandRole(Player __instance, HandTrackingFeature __0, ref HandLocomotionRole __result)
    {
        var active = FlatRatController.Active;
        if (active == null || !active.Owns(__instance))
        {
            return true;
        }

        __result = active.HandRole(__0);
        return false;
    }

    public static void BeforePlayerPhysics(Player __instance)
    {
        var active = FlatRatController.Active;
        if (active != null && active.Owns(__instance))
        {
            try { active.BeforeNativePhysics(); }
            catch (Exception ex) { active.HandleFailure(ex); }
        }
    }

    public static void AfterPlayerPhysics(Player __instance)
    {
        var active = FlatRatController.Active;
        if (active != null && active.Owns(__instance))
        {
            try { active.AfterNativePhysics(); }
            catch (Exception ex) { active.HandleFailure(ex); }
        }
    }

    public override bool Unload()
    {
        harmony?.UnpatchSelf();
        if (controller != null)
        {
            controller.Shutdown();
            UnityEngine.Object.Destroy(controller);
        }
        return true;
    }
}
