using UnityEngine;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Marks dynamic forest fog occluder geometry. These block FOW raycasts but not gameplay LOS.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatForestFogBlocker : MonoBehaviour
    {
    }

}
