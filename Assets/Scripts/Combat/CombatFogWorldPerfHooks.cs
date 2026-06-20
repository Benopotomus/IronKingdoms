using FOW;
using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Sandwiches stock FogOfWarWorld Update/LateUpdate for whole-frame timing without editing FOW assets.
    /// </summary>
    public static class CombatFogWorldPerfHooks
    {
        public static void EnsureAttached(GameObject fogOfWarWorldObject)
        {
            if (fogOfWarWorldObject == null)
            {
                return;
            }

            EnsureComponent<CombatFogWorldPerfUpdateBegin>(fogOfWarWorldObject);
            EnsureComponent<CombatFogWorldPerfUpdateEnd>(fogOfWarWorldObject);
            EnsureComponent<CombatFogWorldPerfLateUpdateBegin>(fogOfWarWorldObject);
            EnsureComponent<CombatFogWorldPerfLateUpdateEnd>(fogOfWarWorldObject);
        }

        private static void EnsureComponent<T>(GameObject target) where T : Component
        {
            if (target.GetComponent<T>() == null)
            {
                target.AddComponent<T>();
            }
        }
    }

    [DefaultExecutionOrder(-32000)]
    internal sealed class CombatFogWorldPerfUpdateBegin : MonoBehaviour
    {
        private void Update()
        {
            CombatFogPerfLogger.BeginWorldUpdatePhase1();
        }
    }

    [DefaultExecutionOrder(32000)]
    internal sealed class CombatFogWorldPerfUpdateEnd : MonoBehaviour
    {
        private void Update()
        {
            CombatFogPerfLogger.EndWorldUpdatePhase1();
        }
    }

    [DefaultExecutionOrder(-32000)]
    internal sealed class CombatFogWorldPerfLateUpdateBegin : MonoBehaviour
    {
        private void LateUpdate()
        {
            CombatFogPerfLogger.BeginWorldLateUpdate();
        }
    }

    [DefaultExecutionOrder(32000)]
    internal sealed class CombatFogWorldPerfLateUpdateEnd : MonoBehaviour
    {
        private void LateUpdate()
        {
            CombatFogPerfLogger.EndWorldLateUpdate(FogOfWarWorld.instance);
        }
    }
}
