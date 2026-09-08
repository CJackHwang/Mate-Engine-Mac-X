using System.Collections.Generic;
using Unity.Pipeline.Config;
using UnityEngine;

namespace Unity.Pipeline
{
    /// <summary>
    /// Creates the runtime Pipeline driver automatically when a Player boots (or Play Mode is
    /// entered in the Editor) — no scene setup required. Reads RuntimePipelineConfig (JSON-backed
    /// in the Editor, a build-baked Resources asset in a Player) and the build-baked
    /// RuntimePipelineBuildInfo from Resources; does nothing if no config exists or the config
    /// has the server disabled.
    /// </summary>
    public static class RuntimePipelineBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RuntimeBootstrap()
        {
            Bootstrap();
        }

        /// <summary>
        /// The driver created by <see cref="Bootstrap"/>, or null if it has not run yet (or created
        /// nothing because there was no usable config). Reachable for tests and manual control.
        /// </summary>
        public static RuntimePipelineDriver Instance { get; internal set; }

        /// <summary>
        /// Core bootstrap logic, split out from the attributed entry point so tests can invoke it
        /// directly instead of waiting for a real domain/Player boot. Returns the created driver,
        /// or null if the server should not start (no config asset, or disabled). Idempotent: a
        /// second call returns the existing <see cref="Instance"/> rather than creating a duplicate.
        /// </summary>
        /// <returns>The created (or already-existing) driver, or null if the server should not start.</returns>
        public static RuntimePipelineDriver Bootstrap()
        {
            if (Instance != null)
                return Instance;

            var config = RuntimePipelineConfig.Load();
            if (config == null || !config.enableInBuilds)
                return null;

            var buildInfo = RuntimePipelineBuildInfo.Load();
            IReadOnlyList<string> roots = buildInfo != null
                ? (IReadOnlyList<string>)buildInfo.allowedReloadRoots
                : System.Array.Empty<string>();
            // Empty when there's no build info (e.g. play mode never built): the driver then rejects
            // every push, which is the correct strict default outside a real signed build.
            string pushPublicKey = buildInfo != null ? buildInfo.pushPublicKey : "";

            var go = new GameObject("Pipeline Runtime Driver") { hideFlags = HideFlags.DontSave };
            var driver = go.AddComponent<RuntimePipelineDriver>();
            driver.Initialize(config, roots, pushPublicKey);
            Instance = driver;
            return driver;
        }
    }
}
