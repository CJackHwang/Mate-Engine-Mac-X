using System.Collections.Generic;
using NUnit.Framework;
using Unity.Pipeline.Config;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Tests.Editor
{
    class RuntimePipelineBuildInfoTests
    {
        private const string TestAssetPath = "Assets/Resources/RuntimePipelineBuildInfo.asset";

        [SetUp]
        public void SetUp()
        {
            Assert.IsNull(RuntimePipelineBuildInfo.Load(),
                "A RuntimePipelineBuildInfo asset already exists somewhere under a Resources folder in " +
                "this project; these tests require a clean slate. Remove it before running.");
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.LoadAssetAtPath<RuntimePipelineBuildInfo>(TestAssetPath) != null)
                AssetDatabase.DeleteAsset(TestAssetPath);
        }

        [Test]
        public void DefaultValue_AllowedReloadRootsIsEmpty()
        {
            var info = ScriptableObject.CreateInstance<RuntimePipelineBuildInfo>();

            Assert.IsNotNull(info.allowedReloadRoots);
            Assert.AreEqual(0, info.allowedReloadRoots.Count);

            Object.DestroyImmediate(info);
        }

        [Test]
        public void Load_AssetInResourcesFolder_FindsItWithRoots()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var info = ScriptableObject.CreateInstance<RuntimePipelineBuildInfo>();
            info.allowedReloadRoots = new List<string> { "C:/fake/project/Assets" };
            AssetDatabase.CreateAsset(info, TestAssetPath);
            AssetDatabase.SaveAssets();

            var loaded = RuntimePipelineBuildInfo.Load();

            Assert.IsNotNull(loaded);
            CollectionAssert.AreEqual(new[] { "C:/fake/project/Assets" }, loaded.allowedReloadRoots);
        }

        [Test]
        public void Load_NoAsset_ReturnsNull()
        {
            Assert.IsNull(RuntimePipelineBuildInfo.Load());
        }
    }
}
