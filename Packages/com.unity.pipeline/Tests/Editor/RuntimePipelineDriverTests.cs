using NUnit.Framework;
using Unity.Pipeline.Config;
using Unity.Pipeline.Models;
using Unity.Pipeline.Runtime.Telemetry;
using Unity.Pipeline.Tests.Editor.ServerLifecyle;
using UnityEngine;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Tests for RuntimePipelineDriver, created automatically by RuntimePipelineBootstrap instead
    /// of being authored in a scene.
    /// </summary>
    /// <remarks>
    /// Excluded from the default (dogfood) run: these tests start a real RuntimePipelineDriver /
    /// runtime server and mutate global command discovery, which destabilizes the live editor
    /// server agents drive over HTTP. Run them deliberately from the Test Runner window.
    /// </remarks>
    [Explicit("Starts a real RuntimePipelineDriver/server and mutates global state; would destabilize the live server. Run manually from the Test Runner window.")]
    [Category("ServerLifecycle")]
    class RuntimePipelineDriverTests
    {
        private GameObject m_TestGameObject;
        private RuntimePipelineDriver m_Driver;
        private RuntimePipelineConfig m_Config;
        private bool m_OriginalRunInBackground;

        [SetUp]
        public void SetUp()
        {
            GlobalServerStateGuard.Capture();

            m_OriginalRunInBackground = Application.runInBackground;

            m_Config = ScriptableObject.CreateInstance<RuntimePipelineConfig>();

            m_TestGameObject = new GameObject("TestRuntimePipelineDriver");
            m_Driver = m_TestGameObject.AddComponent<RuntimePipelineDriver>();
            m_Driver.Initialize(m_Config, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Driver != null && m_Driver.IsServerRunning)
                m_Driver.StopServer();

            if (m_TestGameObject != null)
                Object.DestroyImmediate(m_TestGameObject);

            if (m_Config != null)
                Object.DestroyImmediate(m_Config);

            Application.runInBackground = m_OriginalRunInBackground;

            GlobalServerStateGuard.Restore();
        }

        [Test]
        public void ServerLifecycle_EnabledInBuilds_CanStartAndStop()
        {
            m_Config.enableInBuilds = true;

            m_Driver.StartServer();

            Assert.IsTrue(m_Driver.IsServerRunning, "Server should be running");
            Assert.Greater(m_Driver.ActualPort, 0, "Should have assigned port");
            Assert.NotNull(m_Driver.Server, "Server instance should exist");

            m_Driver.StopServer();

            Assert.IsFalse(m_Driver.IsServerRunning, "Server should be stopped");
        }

        [Test]
        public void ServerLifecycle_StartStop_RestoresRunInBackground()
        {
            Application.runInBackground = false;
            m_Config.enableInBuilds = true;

            m_Driver.StartServer();

            Assert.IsTrue(Application.runInBackground, "Starting the server should force runInBackground on");

            m_Driver.StopServer();

            Assert.IsFalse(Application.runInBackground, "Stopping the server should restore the previous runInBackground value");
        }

        [Test]
        public void ServerLifecycle_StoppedExternallyBeforeStopServerCalled_StillRestoresRunInBackground()
        {
            Application.runInBackground = false;
            m_Config.enableInBuilds = true;

            m_Driver.StartServer();
            Assert.IsTrue(Application.runInBackground, "Precondition: starting the server should force runInBackground on");

            // Simulate the listener dying on its own (e.g. an unexpected native failure) rather
            // than being stopped through the driver: stop the server directly, bypassing
            // RuntimePipelineDriver.StopServer() entirely, so IsRunning goes false before the
            // driver's own StopServer ever runs.
            m_Driver.Server.Stop();
            Assert.IsFalse(m_Driver.IsServerRunning, "Precondition: the server is no longer running");

            m_Driver.StopServer();

            Assert.IsFalse(Application.runInBackground,
                "StopServer must still restore runInBackground even when the server had already stopped on its own");
        }

        [Test]
        public void ServerStart_AlreadyOverriddenByHotReloadPush_DoesNotClobberSavedValue()
        {
            // Simulates a hot-reload push arriving over PlayerConnection (which needs no HTTP
            // server) before StartServer ever ran, so DrainPendingReloads already forced
            // runInBackground on and saved the real original value.
            Application.runInBackground = false;
            SetRunInBackgroundOverrideState(m_Driver, previousValue: false, overridden: true);
            Application.runInBackground = true;

            m_Config.enableInBuilds = true;
            m_Driver.StartServer();

            Assert.IsTrue(Application.runInBackground, "Precondition: still forced on while the server starts");

            m_Driver.StopServer();

            Assert.IsFalse(Application.runInBackground,
                "StopServer must restore the value saved before the hot-reload push overrode it, " +
                "not the already-overridden 'true' StartServer would otherwise re-save on top of it");
        }

        [Test]
        public void Configuration_ValidSettings_PassesValidation()
        {
            m_Config.enableInBuilds = true;

            var result = m_Driver.ValidateConfiguration();

            Assert.IsTrue(result.IsValid, $"Should pass validation: {result.Message}");
        }

        [Test]
        public void Token_AutoGenerated_IsSecure()
        {
            var config = ScriptableObject.CreateInstance<RuntimePipelineConfig>();

            var descriptor = RuntimeInstanceDescriptor.CreateCurrent(7900, config);

            Assert.IsNotEmpty(descriptor.EvalToken, "Token should not be empty");
            Assert.GreaterOrEqual(descriptor.EvalToken.Length, 32, "Token should be at least 32 characters for security");

            Object.DestroyImmediate(config);
        }

        [Test]
        public void ServerStart_DisabledInBuilds_DoesNotStart()
        {
            m_Config.enableInBuilds = false;

            m_Driver.StartServer();

            Assert.IsFalse(m_Driver.IsServerRunning, "Server should not start when disabled");
        }

        [Test]
        public void ServerStart_AlreadyStarted_DoesNotStartAgain()
        {
            m_Config.enableInBuilds = true;
            m_Driver.StartServer();

            var firstPort = m_Driver.ActualPort;

            m_Driver.StartServer();

            Assert.AreEqual(firstPort, m_Driver.ActualPort, "Port should not change when starting already started server");
        }

        [Test]
        public void SecondDriver_DoesNotDisposeFirstDriversSampler()
        {
            // RuntimePipelineDriver has no [ExecuteAlways], so Unity never invokes Awake/OnDestroy
            // automatically for it in Edit Mode (only in Play Mode or a real Player) — confirmed by
            // every other test in this fixture driving StartServer/ValidateConfiguration directly
            // instead of relying on Awake's side effects. Invoke them via reflection here to exercise
            // the same sampler-ownership logic a running game would trigger on its own.
            InvokeAwake(m_Driver); // m_Driver is from SetUp; this is its first Awake.

            var sharedBeforeSecondDriver = FrameStatsSampler.Shared;
            Assert.IsNotNull(sharedBeforeSecondDriver, "Precondition: the first driver should already own a sampler");

            var secondConfig = ScriptableObject.CreateInstance<RuntimePipelineConfig>();
            var secondGo = new GameObject("SecondTestDriver");
            var secondDriver = secondGo.AddComponent<RuntimePipelineDriver>();
            secondDriver.Initialize(secondConfig, null);
            InvokeAwake(secondDriver);

            Assert.AreSame(sharedBeforeSecondDriver, FrameStatsSampler.Shared,
                "A second driver's Awake must not replace an already-owned sampler");

            InvokeOnDestroy(secondDriver);
            Object.DestroyImmediate(secondGo);

            Assert.AreSame(sharedBeforeSecondDriver, FrameStatsSampler.Shared,
                "Destroying the second (non-owning) driver must not dispose the first driver's sampler");

            Object.DestroyImmediate(secondConfig);

            // m_Driver owns the sampler; TearDown's DestroyImmediate would not otherwise trigger its
            // OnDestroy in Edit Mode, so do it explicitly to avoid leaking the sampler into other tests.
            InvokeOnDestroy(m_Driver);
        }

        /// <summary>
        /// Force a MonoBehaviour message that Unity would otherwise only dispatch automatically in
        /// Play Mode (RuntimePipelineDriver has no [ExecuteAlways]).
        /// </summary>
        private static void InvokeMessage(RuntimePipelineDriver driver, string methodName)
        {
            var method = typeof(RuntimePipelineDriver).GetMethod(methodName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            method.Invoke(driver, null);
        }

        private static void InvokeAwake(RuntimePipelineDriver driver) => InvokeMessage(driver, "Awake");

        private static void InvokeOnDestroy(RuntimePipelineDriver driver) => InvokeMessage(driver, "OnDestroy");

        /// <summary>
        /// Force the driver's runInBackground save/restore bookkeeping into a given state, to set up
        /// scenarios (like a hot-reload push racing StartServer) that would otherwise require decoding
        /// a real PlayerConnection payload to reach through DrainPendingReloads.
        /// </summary>
        private static void SetRunInBackgroundOverrideState(RuntimePipelineDriver driver, bool previousValue, bool overridden)
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(RuntimePipelineDriver).GetField("m_PreviousRunInBackground", flags).SetValue(driver, previousValue);
            typeof(RuntimePipelineDriver).GetField("m_RunInBackgroundOverridden", flags).SetValue(driver, overridden);
        }
    }
}
