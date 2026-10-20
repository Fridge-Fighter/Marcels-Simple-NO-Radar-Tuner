using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace RadarTuner
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class RadarTunerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.nuclearoption.radartuner";
        public const string PluginName = "Radar Tuner";
        public const string PluginVersion = "1.0.0";

        internal static RadarTunerPlugin Instance;

        internal static ConfigEntry<float> RangeMultiplier;
        internal static ConfigEntry<float> AntiStealth;
        internal static ConfigEntry<float> MinSignalMultiplier;
        internal static ConfigEntry<float> ClutterMultiplier;
        internal static ConfigEntry<float> DopplerMultiplier;
        internal static ConfigEntry<bool> AffectMissileSeekers;

        private void Awake()
        {
            Instance = this;

            RangeMultiplier = Config.Bind(
                "Radar",
                "RangeMultiplier",
                1f,
                new ConfigDescription(
                    "Multiplies every radar's max range. 1 = vanilla, 2 = double, 0.5 = half. Line of sight and the radar horizon still apply.",
                    new AcceptableValueRange<float>(0.05f, 20f)));

            AntiStealth = Config.Bind(
                "Radar",
                "AntiStealth",
                0f,
                new ConfigDescription(
                    "How much stealth (radar cross-section) matters. 0 = vanilla. 1 = stealth is ignored. -1 = stealth is stronger than vanilla.",
                    new AcceptableValueRange<float>(-1f, 1f)));

            MinSignalMultiplier = Config.Bind(
                "Radar",
                "MinSignalMultiplier",
                1f,
                new ConfigDescription(
                    "Multiplies the weakest return a radar will accept. Lower than 1 detects fainter contacts. 1 = vanilla.",
                    new AcceptableValueRange<float>(0.05f, 10f)));

            ClutterMultiplier = Config.Bind(
                "Radar",
                "ClutterMultiplier",
                1f,
                new ConfigDescription(
                    "Multiplies ground-clutter penalty. Lower than 1 = better look-down. 0 = ignore ground clutter. 1 = vanilla.",
                    new AcceptableValueRange<float>(0f, 5f)));

            DopplerMultiplier = Config.Bind(
                "Radar",
                "DopplerMultiplier",
                1f,
                new ConfigDescription(
                    "Multiplies the speed bonus on a radar return. 1 = vanilla.",
                    new AcceptableValueRange<float>(0f, 5f)));

            AffectMissileSeekers = Config.Bind(
                "Radar",
                "AffectMissileSeekers",
                true,
                "Also tune active-radar (ARH) missile seekers. Semi-active missiles copy the shooter's radar either way.");

            RangeMultiplier.SettingChanged += OnChanged;
            AntiStealth.SettingChanged += OnChanged;
            MinSignalMultiplier.SettingChanged += OnChanged;
            ClutterMultiplier.SettingChanged += OnChanged;
            DopplerMultiplier.SettingChanged += OnChanged;
            AffectMissileSeekers.SettingChanged += OnChanged;

            Harmony.CreateAndPatchAll(typeof(RadarTunerPlugin).Assembly, PluginGuid);
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private static void OnChanged(object sender, System.EventArgs args)
        {
            RadarScaling.ReapplyAll();
        }
    }

    static class RadarScaling
    {
        static readonly Dictionary<Radar, RadarParams> RadarOriginals = new Dictionary<Radar, RadarParams>();
        static readonly Dictionary<ARHSeeker, RadarParams> SeekerOriginals = new Dictionary<ARHSeeker, RadarParams>();
        static readonly FieldInfo SeekerParamsField = AccessTools.Field(typeof(ARHSeeker), "radarParameters");

        public static float TuneRcs(float rcs)
        {
            float anti = RadarTunerPlugin.AntiStealth.Value;
            if (anti == 0f || rcs <= 0f)
                return rcs;
            return Mathf.Pow(rcs, 1f - anti);
        }

        public static void ApplyRadar(Radar radar)
        {
            if (radar == null)
                return;
            SweepRadars();
            RadarParams original;
            if (!RadarOriginals.TryGetValue(radar, out original))
            {
                original = radar.RadarParameters;
                RadarOriginals.Add(radar, original);
            }
            radar.RadarParameters = Scale(original);
        }

        public static void ApplySeeker(ARHSeeker seeker)
        {
            if (seeker == null || SeekerParamsField == null)
                return;
            SweepSeekers();
            RadarParams original;
            if (!SeekerOriginals.TryGetValue(seeker, out original))
            {
                original = (RadarParams)SeekerParamsField.GetValue(seeker);
                SeekerOriginals.Add(seeker, original);
            }
            RadarParams tuned = RadarTunerPlugin.AffectMissileSeekers.Value ? Scale(original) : original;
            SeekerParamsField.SetValue(seeker, tuned);
        }

        public static void ReapplyAll()
        {
            var deadRadars = new List<Radar>();
            foreach (KeyValuePair<Radar, RadarParams> pair in RadarOriginals)
            {
                if (pair.Key == null)
                    deadRadars.Add(pair.Key);
                else
                    pair.Key.RadarParameters = Scale(pair.Value);
            }
            foreach (Radar dead in deadRadars)
                RadarOriginals.Remove(dead);

            if (SeekerParamsField == null)
                return;
            var deadSeekers = new List<ARHSeeker>();
            bool tuneSeekers = RadarTunerPlugin.AffectMissileSeekers.Value;
            foreach (KeyValuePair<ARHSeeker, RadarParams> pair in SeekerOriginals)
            {
                if (pair.Key == null)
                    deadSeekers.Add(pair.Key);
                else
                    SeekerParamsField.SetValue(pair.Key, tuneSeekers ? Scale(pair.Value) : pair.Value);
            }
            foreach (ARHSeeker dead in deadSeekers)
                SeekerOriginals.Remove(dead);
        }

        static void SweepRadars()
        {
            if (RadarOriginals.Count < 32)
                return;
            List<Radar> dead = null;
            foreach (Radar radar in RadarOriginals.Keys)
            {
                if (radar == null)
                {
                    if (dead == null)
                        dead = new List<Radar>();
                    dead.Add(radar);
                }
            }
            if (dead == null)
                return;
            foreach (Radar radar in dead)
                RadarOriginals.Remove(radar);
        }

        static void SweepSeekers()
        {
            if (SeekerOriginals.Count < 32)
                return;
            List<ARHSeeker> dead = null;
            foreach (ARHSeeker seeker in SeekerOriginals.Keys)
            {
                if (seeker == null)
                {
                    if (dead == null)
                        dead = new List<ARHSeeker>();
                    dead.Add(seeker);
                }
            }
            if (dead == null)
                return;
            foreach (ARHSeeker seeker in dead)
                SeekerOriginals.Remove(seeker);
        }

        static RadarParams Scale(RadarParams original)
        {
            original.maxRange *= RadarTunerPlugin.RangeMultiplier.Value;
            original.minSignal *= RadarTunerPlugin.MinSignalMultiplier.Value;
            original.clutterFactor *= RadarTunerPlugin.ClutterMultiplier.Value;
            original.dopplerFactor *= RadarTunerPlugin.DopplerMultiplier.Value;
            return original;
        }
    }

    [HarmonyPatch(typeof(Radar), "Awake")]
    static class RadarAwakePatch
    {
        static void Postfix(Radar __instance)
        {
            RadarScaling.ApplyRadar(__instance);
        }
    }

    [HarmonyPatch(typeof(Radar), nameof(Radar.AttachToUnit))]
    static class RadarAttachPatch
    {
        static void Postfix(Radar __instance)
        {
            RadarScaling.ApplyRadar(__instance);
        }
    }

    [HarmonyPatch(typeof(ARHSeeker), "Initialize")]
    static class ArhInitializePatch
    {
        static void Postfix(ARHSeeker __instance)
        {
            RadarScaling.ApplySeeker(__instance);
        }
    }

    [HarmonyPatch(typeof(ARHSeeker), "GetRadarReturn")]
    static class ArhGetReturnPatch
    {
        static void Prefix(ARHSeeker __instance)
        {
            RadarScaling.ApplySeeker(__instance);
        }
    }

    [HarmonyPatch(typeof(RadarParams), nameof(RadarParams.GetSignalStrength))]
    static class SignalStrengthPatch
    {
        static void Prefix(ref float RCS)
        {
            RCS = RadarScaling.TuneRcs(RCS);
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.EstimateDetection))]
    static class EstimateDetectionPatch
    {
        static bool Prefix(Aircraft __instance, Radar radar, out float returnSignal, ref bool __result)
        {
            returnSignal = 0f;
            if (radar == null)
            {
                __result = false;
                return false;
            }

            float distance = FastMath.Distance(radar.transform.position, __instance.transform.position);
            RadarParams parameters = radar.RadarParameters;
            float clutter = __instance.maxRadius * __instance.maxRadius * 2f / (__instance.radarAlt * __instance.radarAlt);
            float rcs = RadarScaling.TuneRcs(__instance.RCS);
            returnSignal = parameters.maxRange / distance * Mathf.Pow(rcs, 0.25f);
            returnSignal = Mathf.Min(returnSignal, parameters.maxSignal);
            returnSignal -= clutter * 0.15f;
            returnSignal = Mathf.Max(returnSignal, 0f);
            __result = returnSignal > parameters.minSignal;
            return false;
        }
    }
}