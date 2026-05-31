using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace FOW.EditorStuff
{
    [InitializeOnLoad]
    public static class RecompileDetector
    {
        const string Key = "FOW_FogOfWarWorld_GlobalObjectId";

        static RecompileDetector()
        {
            CompilationPipeline.compilationStarted += _ => ShutdownForReload();
            AssemblyReloadEvents.beforeAssemblyReload += ShutdownForReload;

            AssemblyReloadEvents.afterAssemblyReload += () =>
            {
                EditorApplication.delayCall -= StartAfterReload;
                EditorApplication.delayCall += StartAfterReload;
            };
        }

        public static void ShutdownForReload()
        {
            if (!Application.isPlaying) return;

            var world = FogOfWarWorld.instance;
            if (world == null) return;

            var gid = GlobalObjectId.GetGlobalObjectIdSlow(world);
            SessionState.SetString(Key, gid.ToString());

            world.enabled = false;
        }

        public static void StartAfterReload()
        {
            if (!Application.isPlaying) return;

            var gidString = SessionState.GetString(Key, string.Empty);
            FogOfWarWorld world = null;

            if (!string.IsNullOrEmpty(gidString) && GlobalObjectId.TryParse(gidString, out var gid))
                world = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) as FogOfWarWorld;

            if (world == null) return;

            world.enabled = true;
        }

    }
}