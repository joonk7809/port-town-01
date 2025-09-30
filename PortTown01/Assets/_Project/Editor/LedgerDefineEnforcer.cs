// SPDX-License-Identifier: MIT
// Port Town 01 — Editor utility to enforce LEDGER_ENABLED across build targets.
// Place under Assets/_Project/Editor/ to compile in the UnityEditor assembly.

#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PortTown01.Editor
{
    [InitializeOnLoad]
    public static class LedgerDefineEnforcer
    {
        private const string Symbol = "LEDGER_ENABLED";

        static LedgerDefineEnforcer()
        {
            // Enforce on domain reload for common targets.
            TryEnsureForGroup(BuildTargetGroup.Standalone);
#if UNITY_WEBGL
            TryEnsureForGroup(BuildTargetGroup.WebGL);
#endif
#if UNITY_ANDROID
            TryEnsureForGroup(BuildTargetGroup.Android);
#endif
#if UNITY_IOS
            TryEnsureForGroup(BuildTargetGroup.iOS);
#endif
        }

        [MenuItem("PortTown/Defines/Enable LEDGER_ENABLED (All Groups)")]
        private static void EnableForAllGroups()
        {
            foreach (BuildTargetGroup g in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (g == BuildTargetGroup.Unknown) continue;
                TryEnsureForGroup(g);
            }
            Debug.Log("[LedgerDefineEnforcer] LEDGER_ENABLED added to all build target groups.");
        }

        [MenuItem("PortTown/Defines/Disable LEDGER_ENABLED (All Groups)")]
        private static void DisableForAllGroups()
        {
            foreach (BuildTargetGroup g in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (g == BuildTargetGroup.Unknown) continue;
                TryRemoveForGroup(g);
            }
            Debug.Log("[LedgerDefineEnforcer] LEDGER_ENABLED removed from all build target groups.");
        }

        private static void TryEnsureForGroup(BuildTargetGroup group)
        {
            try
            {
                var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group) ?? string.Empty;
                var list = defines.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                if (!list.Contains(Symbol))
                {
                    list.Add(Symbol);
                    var joined = string.Join(";", list);
                    PlayerSettings.SetScriptingDefineSymbolsForGroup(group, joined);
                }
            }
            catch { /* ignore unsupported/obsolete groups */ }
        }

        private static void TryRemoveForGroup(BuildTargetGroup group)
        {
            try
            {
                var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group) ?? string.Empty;
                var list = defines.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0 && s != Symbol).ToList();
                var joined = string.Join(";", list);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, joined);
            }
            catch { /* ignore unsupported/obsolete groups */ }
        }
    }

    // Optional: guard against builds that somehow drop the define.
    // This pre-build step re-applies the symbol for the active build target.
    public sealed class LedgerDefinePreBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => int.MinValue;
        public void OnPreprocessBuild(BuildReport report)
        {
            var group = report.summary.platformGroup;
            typeof(LedgerDefineEnforcer)
                .GetMethod("TryEnsureForGroup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.Invoke(null, new object[] { group });
        }
    }
}
#endif
