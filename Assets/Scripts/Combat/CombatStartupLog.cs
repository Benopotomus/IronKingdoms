using System;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Filter console output with "[CombatStartup]" when tracing match load / unit spawn failures.
    /// </summary>
    internal static class CombatStartupLog
    {
        internal static void Log(string message)
        {
            Debug.Log($"[CombatStartup] {message}");
        }

        internal static void LogWarning(string message)
        {
            Debug.LogWarning($"[CombatStartup] {message}");
        }

        internal static void LogError(string message)
        {
            Debug.LogError($"[CombatStartup] {message}");
        }

        internal static void LogException(string phase, Exception exception)
        {
            Debug.LogError($"[CombatStartup] Phase '{phase}' threw {exception.GetType().Name}: {exception.Message}");
            Debug.LogException(exception);
        }
    }
}
