using NUnit.Framework;
using Unity.Pipeline.HotReload;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Round-trip tests for the editor→player hot-reload push wire format
    /// ({typeName, methodNames, il}).
    /// </summary>
    class HotReloadConnectWireTests
    {
        static readonly byte[] k_Il = { 1, 2, 3, 4, 5 };

        [Test]
        public void Encode_RoundTrips()
        {
            var payload = PipelineHotReloadConnect.Encode("MyType", new[] { "A", "B" }, k_Il);

            Assert.IsTrue(PipelineHotReloadConnect.TryDecode(
                payload, out var typeName, out var methods, out var il));
            Assert.AreEqual("MyType", typeName);
            CollectionAssert.AreEqual(new[] { "A", "B" }, methods);
            CollectionAssert.AreEqual(k_Il, il);
        }

        [Test]
        public void Encode_EmptyIl_RoundTrips()
        {
            var payload = PipelineHotReloadConnect.Encode("T", new[] { "A" }, null);

            Assert.IsTrue(PipelineHotReloadConnect.TryDecode(
                payload, out var typeName, out var methods, out var il));
            Assert.AreEqual("T", typeName);
            CollectionAssert.AreEqual(new[] { "A" }, methods);
            Assert.AreEqual(0, il.Length);
        }

        [Test]
        public void TruncatedPayload_FailsCleanly()
        {
            var payload = PipelineHotReloadConnect.Encode("MyType", new[] { "A" }, k_Il);

            for (int cut = 1; cut < payload.Length; cut++)
            {
                var truncated = new byte[payload.Length - cut];
                System.Array.Copy(payload, truncated, truncated.Length);
                // Some truncations still parse as a shorter-but-valid IL section; the invariant
                // that matters is no exception and no torn decode reporting success with the
                // full IL length.
                if (PipelineHotReloadConnect.TryDecode(truncated, out _, out _, out var il))
                    Assert.Less(il.Length, k_Il.Length);
            }
        }
    }
}
