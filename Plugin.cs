using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using SharedLib;

using System;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using Atomicrops.Game.Upgrades;
using Atomicrops.Game.Data;
using Atomicrops.Game.Player;
using Atomicrops.Core.Upgrades;


namespace SynergiesAPI
{

    public static class MyPluginInfo
    {
        public const string PLUGIN_GUID = "pauli.plugin.SynergiesAPI";
        public const string PLUGIN_NAME = "SynergiesAPI";
        public const string PLUGIN_VERSION = "1.0.0";
    }

    public class ActionContainer
    {
        public Action Function { get; set; }
        public Action Cleanup { get; set; }

        public ActionContainer(Action actionMethod, Action cleanupMethod)
        {
            Function = actionMethod;
            Cleanup = cleanupMethod;
        }
    }


    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static BepInEx.Logging.ManualLogSource Log;
        public static Plugin Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            HookFloatTextPrefab();

            Log = Logger;
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll();

        }

        private void HookFloatTextPrefab()
        {
            var prefab = Resources.Load<GameObject>("InGameFloatTextNumberOnly");

            if (prefab != null && prefab.GetComponent<RainbowFloatText>() == null)
            {
                var r = prefab.AddComponent<RainbowFloatText>();
                r.enabled = false;
            }
        }


    }

    public class RainbowFloatText : MonoBehaviour
    {
        public float speed = 1.2f;      // hue cycle speed
        public float saturation = 1f;   // color intensity
        public float brightness = 1f;   // value (brightness)

        private bool isRunning = false;
        private Text numberText;

        void Awake()
        {
            // Works for both InGameFloatTextNumberOnly prefab (UI.Text) and children
            numberText = GetComponentInChildren<Text>(true);
        }

        public void Begin(float duration)
        {
            isRunning = true;
            Invoke(nameof(Stop), duration);
        }

        private void Stop()
        {
            isRunning = false;
            if (numberText != null)
                numberText.color = Color.white;  // reset to vanilla look
        }

        void Update()
        {
            if (!isRunning || numberText == null) return;

            // Convert current color to HSV, rotate the hue, and force strong saturation
            Color.RGBToHSV(numberText.color, out float h, out float s, out float v);
            h = (h + Time.deltaTime * speed) % 1f;
            s = saturation;
            v = brightness;
            numberText.color = Color.HSVToRGB(h, s, v);
        }
    }

    public class Synergy
    {
        public static List<Synergy> AllSynergies = new List<Synergy>();

        public static Dictionary<string, List<Synergy>> AllRequiredUpgrades = new Dictionary<string, List<Synergy>>();
        public static HashSet<string> CurrentUpgrades = new HashSet<string>();
        public bool isActive = false;
        public string name;
        public string description;
        public List<string> requiredUpgrades;
        public Action action;
        public Action cleanup;
        public bool hasDonePopup = false;

        public Synergy(string synergyName, string synergyDescription, Action functionalityMethod, Action cleanupMethod, List<string> upgrades)
        {
            name = synergyName;
            description = synergyDescription;
            action = functionalityMethod;
            cleanup = cleanupMethod;
            requiredUpgrades = new List<string>();

            foreach (string upgrade in upgrades)
            {
                string u = upgrade.Trim(new char[] { ' ', '#' }).ToLower();
                requiredUpgrades.Add(u);

                if (!AllRequiredUpgrades.TryGetValue(u, out var list))
                {
                    list = new List<Synergy>();
                    AllRequiredUpgrades[u] = list;
                }
                list.Add(this);
            }

            AllSynergies.Add(this);
        }

        public void Activate()
        {
            if (!hasDonePopup)
            {
                // run popup code
                Plugin.Instance.StartCoroutine(ShowText());
                hasDonePopup = true;
            }

            action?.Invoke();
        }

        public IEnumerator ShowText()
        {
            var player = SingletonSceneScope<PlayerComp>.I;
            if (player == null) yield break;

            // 1) SYNERGY! rainbow burst
            InGameText.FloatUpNumberOnly("SYNERGY!", player.Anchors.Head, false, 6f);

            FieldInfo goTmpField = AccessTools.Field(typeof(InGameText), "_goTmp");
            GameObject go = (GameObject)goTmpField.GetValue(null);

            var rainbow = go.GetComponent<RainbowFloatText>();
            if (rainbow == null)
            {
                rainbow = go.AddComponent<RainbowFloatText>();
            }

            rainbow.enabled = true;
            rainbow.Begin(10f);
            yield return new WaitForSeconds(1.5f);

            string items = string.Join(" + ", requiredUpgrades);
            InGameText.FloatUpNumberOnly($"{name}: {items}", player.Anchors.Head, false, 6f);
            yield return new WaitForSeconds(1.5f);

            InGameText.FloatUpNumberOnly(description, player.Anchors.Head, false, 6f);
        }
    }

    [HarmonyPatch(typeof(UpgradeRunner), "ApplyUpgradeList")]
    [Harmony]
    class UpgradeRunner_ApplyUpgradeList_Patch
    {
        [HarmonyPriority(Priority.High)]
        static void Prefix()
        {
            foreach (Synergy synergy in Synergy.AllSynergies)
            {
                if (synergy.isActive)
                {
                    synergy.Activate();
                }
            }
        }
    }

    [HarmonyPatch(typeof(UpgradesCollection), "_addStack")]
    class UpgradesCollection_addStack_Patch
    {
        static void Prefix(UpgradeDef def)
        {
            string displayName = def.LootProperties.DisplayName.Trim().ToLower();

            Synergy.CurrentUpgrades.Add(displayName);

            if (!Synergy.AllRequiredUpgrades.TryGetValue(displayName, out var affectedSynergies))
            {
                // this item does not affect any synergies
                return;
            }

            foreach (Synergy synergy in affectedSynergies)
            {
                bool hasAll = true;
                foreach (string requirement in synergy.requiredUpgrades)
                {
                    if (!Synergy.CurrentUpgrades.Contains(requirement))
                    {
                        hasAll = false;
                        break;
                    }
                }

                if (hasAll)
                {
                    synergy.isActive = true;                
                }
    
            }
        }
    }

    [HarmonyPatch(typeof(GameDataPresets), "NewGameData")]
    class GameDataPresets_NewGameData_Patch
    {
        static void Prefix()
        {
            foreach (Synergy synergy in Synergy.AllSynergies)
            {
                if (synergy.isActive)
                {
                    synergy.cleanup?.Invoke();
                    synergy.isActive = false;
                    synergy.hasDonePopup = false;
                    Synergy.CurrentUpgrades = new HashSet<string>();
                }
            }
        }
    }


}