using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronKingdoms.Combat
{
    /// <summary>
    /// Tracks the additively loaded combat map scene so gameplay objects can share its physics world.
    /// </summary>
    public static class CombatMapSceneProvider
    {
        public const string DefaultCombatMapSceneName = "CombatMapScene";

        private static Scene registeredMapScene;

        public static void RegisterMapScene(Scene mapScene)
        {
            if (mapScene.IsValid() && mapScene.isLoaded)
            {
                registeredMapScene = mapScene;
            }
        }

        public static bool TryGetMapScene(out Scene mapScene)
        {
            if (registeredMapScene.IsValid() && registeredMapScene.isLoaded)
            {
                mapScene = registeredMapScene;
                return true;
            }

            mapScene = SceneManager.GetSceneByName(DefaultCombatMapSceneName);
            if (mapScene.IsValid() && mapScene.isLoaded)
            {
                registeredMapScene = mapScene;
                return true;
            }

            return false;
        }

        public static bool TryGetMapPhysicsScene(out PhysicsScene physicsScene)
        {
            if (!TryGetMapScene(out _))
            {
                physicsScene = default;
                return false;
            }

            physicsScene = registeredMapScene.GetPhysicsScene();
            return physicsScene.IsValid();
        }

        public static void MoveToMapScene(GameObject instance)
        {
            if (instance == null || !TryGetMapScene(out var mapScene))
            {
                return;
            }

            if (instance.transform.parent != null)
            {
                instance.transform.SetParent(null, true);
            }

            if (instance.scene == mapScene)
            {
                return;
            }

            SceneManager.MoveGameObjectToScene(instance, mapScene);
        }
    }
}
