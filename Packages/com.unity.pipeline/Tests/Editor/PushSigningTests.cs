using System;
using System.IO;
using NUnit.Framework;
using Unity.Pipeline.Editor;
using Unity.Pipeline.Editor.BuildProcessors;
using Unity.Pipeline.HotReload;
using UnityEngine;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Push-signing coverage, tiers 1–2 of the plan. <see cref="PushSigningEndToEndTests"/> wires
    /// these primitives into the full push chain (build bake → session nonce → sign → verify →
    /// decode) plus the adversary cases. Tier 3 (real editor↔device pass over a socket) is manual:
    ///   1. Build a Development Build; the build log must show "push-signing key … (fingerprint …)".
    ///   2. Boot the player; its log must show "signed pushes only (key …, verify self-test OK)" —
    ///      a self-test failure here means the platform's RSA is unusable (file a bug, don't ship).
    ///   3. push_reload a [HotReload] method; the edit must apply and ack "applied N method(s)".
    ///   4. Force a domain reload in the editor (edit any script), push again — the push must be
    ///      deferred briefly ("awaiting player handshake") and then apply.
    ///   5. Replay check: capture an ApplyReloadMsg with a proxy and resend it — the player log must
    ///      show a rejected push (CounterNotIncreasing) and the game must be unaffected.
    /// </summary>
    class PushSigningTests
    {
        // RSA-2048 generation is the slow part — one keypair set for the whole fixture.
        private static string s_Private;
        private static string s_Public;
        private static string s_OtherPrivate;

        private static readonly byte[] s_Payload = { 10, 20, 30, 40, 50 };
        private byte[] m_Nonce;

        [OneTimeSetUp]
        public void CreateKeys()
        {
            s_Private = PushEnvelope.CreatePrivateKey();
            s_Public = PushEnvelope.DerivePublicKey(s_Private);
            s_OtherPrivate = PushEnvelope.CreatePrivateKey();
        }

        [SetUp]
        public void CreateNonce() => m_Nonce = PushEnvelope.CreateNonce();

        private PushVerifyStatus Open(byte[] envelope, out byte[] payload, byte[] nonce = null, long last = 0) =>
            PushEnvelope.TryOpen(s_Public, envelope, nonce ?? m_Nonce, last, out _, out payload);

        // ---- Tier 1: envelope happy path and every reject path ----

        [Test]
        public void RoundTrip_OpensWithPayloadAndCounter()
        {
            var envelope = PushEnvelope.Sign(s_Private, m_Nonce, 7, s_Payload);

            var status = PushEnvelope.TryOpen(s_Public, envelope, m_Nonce, 0, out var counter, out var payload);

            Assert.AreEqual(PushVerifyStatus.Ok, status);
            Assert.AreEqual(7, counter);
            CollectionAssert.AreEqual(s_Payload, payload);
        }

        [Test]
        public void EmptyPublicKey_NoKey()
        {
            var envelope = PushEnvelope.Sign(s_Private, m_Nonce, 1, s_Payload);
            Assert.AreEqual(PushVerifyStatus.NoKey,
                PushEnvelope.TryOpen("", envelope, m_Nonce, 0, out _, out _));
        }

        [Test]
        public void TamperedPayload_BadSignature()
        {
            var envelope = PushEnvelope.Sign(s_Private, m_Nonce, 1, s_Payload);
            envelope[4 + PushEnvelope.NonceSize + 8 + 4] ^= 0xFF; // first payload byte
            Assert.AreEqual(PushVerifyStatus.BadSignature, Open(envelope, out _));
        }

        [Test]
        public void TamperedNonceBytes_BadSignature()
        {
            // The nonce is under the signature, so editing it in flight dies at signature check,
            // before the nonce comparison is even reached.
            var envelope = PushEnvelope.Sign(s_Private, m_Nonce, 1, s_Payload);
            envelope[4] ^= 0xFF;
            Assert.AreEqual(PushVerifyStatus.BadSignature, Open(envelope, out _));
        }

        [Test]
        public void TamperedCounter_BadSignature()
        {
            var envelope = PushEnvelope.Sign(s_Private, m_Nonce, 1, s_Payload);
            envelope[4 + PushEnvelope.NonceSize] ^= 0xFF; // low counter byte
            Assert.AreEqual(PushVerifyStatus.BadSignature, Open(envelope, out _));
        }

        [Test]
        public void SignedWithDifferentKey_BadSignature()
        {
            var envelope = PushEnvelope.Sign(s_OtherPrivate, m_Nonce, 1, s_Payload);
            Assert.AreEqual(PushVerifyStatus.BadSignature, Open(envelope, out _));
        }

        [Test]
        public void SignedUnderDifferentDomainTag_BadSignature()
        {
            var envelope = PushEnvelope.Sign(s_Private, m_Nonce, 1, s_Payload, "some-other-feature/v1");
            Assert.AreEqual(PushVerifyStatus.BadSignature, Open(envelope, out _));
        }

        [Test]
        public void WrongSessionNonce_NonceMismatch()
        {
            // Valid signature for nonce A presented to a session expecting nonce B — the replay-on-
            // another-device / stale-editor-table case.
            var envelope = PushEnvelope.Sign(s_Private, PushEnvelope.CreateNonce(), 1, s_Payload);
            Assert.AreEqual(PushVerifyStatus.NonceMismatch, Open(envelope, out _));
        }

        [Test]
        public void ReplayedCounter_CounterNotIncreasing()
        {
            var envelope = PushEnvelope.Sign(s_Private, m_Nonce, 5, s_Payload);
            Assert.AreEqual(PushVerifyStatus.CounterNotIncreasing, Open(envelope, out _, last: 5));
            Assert.AreEqual(PushVerifyStatus.CounterNotIncreasing, Open(envelope, out _, last: 9));
        }

        [Test]
        public void UnknownMagic_Malformed()
        {
            var envelope = PushEnvelope.Sign(s_Private, m_Nonce, 1, s_Payload);
            envelope[0] = (byte)'X';
            Assert.AreEqual(PushVerifyStatus.Malformed, Open(envelope, out _));
        }

        [Test]
        public void TruncatedEnvelope_Malformed()
        {
            var envelope = PushEnvelope.Sign(s_Private, m_Nonce, 1, s_Payload);

            var headerOnly = new byte[10];
            Array.Copy(envelope, headerOnly, headerOnly.Length);
            Assert.AreEqual(PushVerifyStatus.Malformed, Open(headerOnly, out _));

            // Preamble intact but the signature cut off entirely.
            var noSig = new byte[4 + PushEnvelope.NonceSize + 8 + 4 + s_Payload.Length];
            Array.Copy(envelope, noSig, noSig.Length);
            Assert.AreEqual(PushVerifyStatus.Malformed, Open(noSig, out _));
        }

        [Test]
        public void LegacyRawPayload_MalformedAndNotAnEnvelope()
        {
            // What a pre-signing editor sends: the bare PipelineHotReloadConnect payload.
            var legacy = PipelineHotReloadConnect.Encode("SomeType", new[] { "M1" }, new byte[] { 1, 2, 3 });
            Assert.IsFalse(PushEnvelope.LooksLikeEnvelope(legacy));
            Assert.AreEqual(PushVerifyStatus.Malformed, Open(legacy, out _));
        }

        [Test]
        public void Verifier_AcceptsInOrder_RejectsReplay()
        {
            var verifier = new PushVerifier(s_Public, m_Nonce);
            var first = PushEnvelope.Sign(s_Private, m_Nonce, 1, s_Payload);
            var second = PushEnvelope.Sign(s_Private, m_Nonce, 2, s_Payload);

            Assert.AreEqual(PushVerifyStatus.Ok, verifier.Verify(first, out _));
            Assert.AreEqual(PushVerifyStatus.Ok, verifier.Verify(second, out _));
            Assert.AreEqual(PushVerifyStatus.CounterNotIncreasing, verifier.Verify(first, out _),
                "a captured earlier push must not replay after later ones were accepted");
            Assert.AreEqual(PushVerifyStatus.CounterNotIncreasing, verifier.Verify(second, out _));
        }

        [Test]
        public void Fingerprint_StableAcrossHalves_DistinctAcrossKeys()
        {
            Assert.AreEqual(PushEnvelope.Fingerprint(s_Private), PushEnvelope.Fingerprint(s_Public));
            Assert.AreEqual(16, PushEnvelope.Fingerprint(s_Public).Length);
            Assert.AreNotEqual(PushEnvelope.Fingerprint(s_Private), PushEnvelope.Fingerprint(s_OtherPrivate));
        }

        [Test]
        public void VerifySelfTest_PassesOnEditorPlatform()
        {
            Assert.IsTrue(PushEnvelope.VerifySelfTest(s_Public, out var error), error);
        }

        [Test]
        public void TryParse_StripsEnvelopeWithoutCrypto()
        {
            var envelope = PushEnvelope.Sign(s_Private, m_Nonce, 3, s_Payload);
            Assert.IsTrue(PushEnvelope.TryParse(envelope, out var nonce, out var counter, out var payload));
            CollectionAssert.AreEqual(m_Nonce, nonce);
            Assert.AreEqual(3, counter);
            CollectionAssert.AreEqual(s_Payload, payload);
        }

        [Test]
        public void HandshakeMessage_RoundTrips()
        {
            var data = PipelineHotReloadConnect.EncodeHandshake(PushEnvelope.ProtocolVersion, "ab12cd34ab12cd34", m_Nonce);
            Assert.IsTrue(PipelineHotReloadConnect.TryDecodeHandshake(data, out var version, out var fp, out var nonce));
            Assert.AreEqual(PushEnvelope.ProtocolVersion, version);
            Assert.AreEqual("ab12cd34ab12cd34", fp);
            CollectionAssert.AreEqual(m_Nonce, nonce);
        }

        // ---- Tier 2: key file lifecycle and the build-time bake ----

        [Test]
        public void SigningKey_CreatedOnceThenReused()
        {
            var dir = Path.Combine(Path.GetTempPath(), "upp-signing-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.IsFalse(PushSigningKey.TryLoad(dir, out _));

                var first = PushSigningKey.LoadOrCreate(dir, out var created);
                Assert.IsTrue(created);
                Assert.IsTrue(PushEnvelope.IsPrivateKey(first));
                Assert.IsTrue(File.Exists(Path.Combine(dir, PushSigningKey.PublicInfoFileName)),
                    "human-readable public-half file should sit next to the key");

                var second = PushSigningKey.LoadOrCreate(dir, out created);
                Assert.IsFalse(created);
                Assert.AreEqual(first, second, "an existing key must be reused, not regenerated");
                Assert.AreEqual(PushEnvelope.Fingerprint(first), PushEnvelope.Fingerprint(second));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void MalformedKeyFile_TreatedAsUnreadable_AndRegenerated()
        {
            // A file with the right prefix but a corrupt body must not pass TryLoad: the prefix-only
            // check let DerivePublicKey throw later and fail the build instead of recovering.
            var dir = Path.Combine(Path.GetTempPath(), "upp-signing-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, PushSigningKey.KeyFileName), "upp1-rsa:abc");

                Assert.IsFalse(PushSigningKey.TryLoad(dir, out _),
                    "a prefixed-but-unparsable key must be reported unreadable, not loaded");

                // LoadOrCreate follows the documented recovery path: replace it with a valid key.
                var key = PushSigningKey.LoadOrCreate(dir, out var created);
                Assert.IsTrue(created, "an unreadable key must be regenerated, not fail the build");
                Assert.IsTrue(PushEnvelope.IsPrivateKey(key));
                Assert.DoesNotThrow(() => PushEnvelope.DerivePublicKey(key),
                    "the regenerated key must fully parse");
                Assert.IsTrue(PushSigningKey.TryLoad(dir, out _), "the fresh key loads cleanly");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Initialize_SetsKeyAndRoots()
        {
            // Build-time data (roots + push public key) reaches the runtime driver through
            // Initialize — the build processor bakes it into RuntimePipelineBuildInfo and the
            // bootstrap threads it in. (The old scene-component bake seam is gone on this backend.)
            var go = new GameObject("driver-under-test");
            try
            {
                var driver = go.AddComponent<RuntimePipelineDriver>();

                var roots = new System.Collections.Generic.List<string> { "C:/roots/a" };
                driver.Initialize(null, roots, s_Public);

                Assert.AreEqual(s_Public, driver.PushPublicKey);
                CollectionAssert.AreEqual(roots, driver.AllowedReloadRoots);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }

    /// <summary>
    /// End-to-end push-signing scenarios: the pieces the unit tests above cover in isolation, wired
    /// into the chain a hot-reload push actually travels — the build processor bakes the project key
    /// onto the manager that ships in the player; the player announces a fresh session nonce and
    /// builds its verifier from the baked key; the editor signs a real reload payload over that nonce
    /// with a strictly increasing counter; the player verifies and recovers the IL. The adversary
    /// cases (forgery, same-session replay, cross-session replay, legacy unsigned, no baked key) run
    /// against that same wired-up session. Only the socket transport and the platform-gated player
    /// receive branch are out of scope — that is the manual device pass documented above.
    /// </summary>
    class PushSigningEndToEndTests
    {
        // The "project": one persisted keypair, as the build machine would hold.
        private string m_KeyDir;
        private string m_PrivateKey;      // never leaves the build machine
        private string m_BakedPublicKey;  // travels into the player build via RuntimePipelineBuildInfo

        // The "player": a fresh session nonce + verifier built from the baked key, exactly as
        // RuntimePipelineDriver.RegisterReloadReceiver does on a real device.
        private byte[] m_PlayerNonce;
        private PushVerifier m_PlayerVerifier;

        // The "editor push signer": a strictly increasing counter over the player's announced nonce.
        private long m_EditorCounter;

        [SetUp]
        public void SetUpProjectAndPlayer()
        {
            m_KeyDir = Path.Combine(Path.GetTempPath(), "upp-e2e-" + Guid.NewGuid().ToString("N"));
            m_PrivateKey = PushSigningKey.LoadOrCreate(m_KeyDir, out _);

            // The build processor bakes the public half into the build info; the bootstrap threads
            // it into the driver that ships in the player.
            m_BakedPublicKey = BakeKeyOntoDriver(PushEnvelope.DerivePublicKey(m_PrivateKey));

            StartNewPlayerSession();
            m_EditorCounter = 0;
        }

        [TearDown]
        public void RemoveKeyDir()
        {
            if (m_KeyDir != null && Directory.Exists(m_KeyDir)) Directory.Delete(m_KeyDir, true);
        }

        private static string BakeKeyOntoDriver(string publicKey)
        {
            var go = new GameObject("e2e-driver");
            try
            {
                var driver = go.AddComponent<RuntimePipelineDriver>();
                driver.Initialize(null,
                    new System.Collections.Generic.List<string> { "C:/proj/Assets" }, publicKey);
                return driver.PushPublicKey;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // A player boot (or reconnect) announces a fresh nonce and rebuilds its verifier from the
        // baked key; the editor learns the nonce via HandshakeMsg.
        private void StartNewPlayerSession()
        {
            m_PlayerNonce = PushEnvelope.CreateNonce();
            m_PlayerVerifier = new PushVerifier(m_BakedPublicKey, m_PlayerNonce);
        }

        // The editor signs a payload with the project key over the player's nonce, using a strictly
        // increasing counter — the PushSigner path minus the socket Send.
        private byte[] EditorSign(byte[] payload)
        {
            m_EditorCounter = Math.Max(m_EditorCounter + 1, DateTime.UtcNow.Ticks);
            return PushEnvelope.Sign(m_PrivateKey, m_PlayerNonce, m_EditorCounter, payload);
        }

        private static byte[] SampleReload() =>
            PipelineHotReloadConnect.Encode("Game.Enemy", new[] { "Tick", "OnHit" }, new byte[] { 0x2A, 0x00, 0x2B });

        [Test]
        public void SignedPush_VerifiesAndRecoversTheExactReloadPayload()
        {
            var envelope = EditorSign(SampleReload());

            var status = m_PlayerVerifier.Verify(envelope, out var recovered);

            Assert.AreEqual(PushVerifyStatus.Ok, status, "a push signed by the build machine's key must be accepted");
            Assert.IsTrue(PipelineHotReloadConnect.TryDecode(recovered, out var type, out var methods, out var il),
                "the recovered payload must decode back to the reload the editor sent");
            Assert.AreEqual("Game.Enemy", type);
            CollectionAssert.AreEqual(new[] { "Tick", "OnHit" }, methods);
            CollectionAssert.AreEqual(new byte[] { 0x2A, 0x00, 0x2B }, il);
        }

        [Test]
        public void ConsecutivePushes_AllAcceptedInOrder()
        {
            for (int i = 0; i < 5; i++)
                Assert.AreEqual(PushVerifyStatus.Ok, m_PlayerVerifier.Verify(EditorSign(SampleReload()), out _), $"push #{i}");
        }

        [Test]
        public void AttackerWithoutTheKey_Forgery_Rejected()
        {
            // A same-network attacker who never had the project key: their best is their own keypair,
            // signing over the nonce they can sniff from the HandshakeMsg.
            var attackerKey = PushEnvelope.CreatePrivateKey();
            var forged = PushEnvelope.Sign(attackerKey, m_PlayerNonce, DateTime.UtcNow.Ticks, SampleReload());

            Assert.AreEqual(PushVerifyStatus.BadSignature, m_PlayerVerifier.Verify(forged, out var recovered),
                "a push not signed by the trusted key must be rejected");
            Assert.IsNull(recovered, "a rejected push must not surface a usable payload");
        }

        [Test]
        public void AttackerReplaysAcceptedPush_SameSession_Rejected()
        {
            var envelope = EditorSign(SampleReload());
            Assert.AreEqual(PushVerifyStatus.Ok, m_PlayerVerifier.Verify(envelope, out _));

            // Attacker captured the on-wire bytes and resends them verbatim.
            Assert.AreEqual(PushVerifyStatus.CounterNotIncreasing, m_PlayerVerifier.Verify(envelope, out var recovered),
                "a captured envelope must not replay within the same session");
            Assert.IsNull(recovered);
        }

        [Test]
        public void AttackerReplaysAcrossSessions_NonceMismatch()
        {
            // A validly-signed push captured earlier, replayed after the player restarts (new nonce).
            // The nonce is under the signature, so the attacker cannot re-point it at the new session.
            var captured = EditorSign(SampleReload());
            Assert.AreEqual(PushVerifyStatus.Ok, m_PlayerVerifier.Verify(captured, out _));

            StartNewPlayerSession(); // player rebooted; the editor has not re-signed for the new nonce
            Assert.AreEqual(PushVerifyStatus.NonceMismatch, m_PlayerVerifier.Verify(captured, out var recovered),
                "a push bound to the previous session's nonce must not apply after a restart");
            Assert.IsNull(recovered);
        }

        [Test]
        public void LegacyUnsignedPush_Rejected()
        {
            // A pre-signing editor — or an attacker mimicking one — sends the bare reload payload.
            var raw = SampleReload();
            Assert.IsFalse(PushEnvelope.LooksLikeEnvelope(raw));
            Assert.AreEqual(PushVerifyStatus.Malformed, m_PlayerVerifier.Verify(raw, out var recovered),
                "an unsigned raw payload must be rejected");
            Assert.IsNull(recovered);
        }

        [Test]
        public void DriverThatMissedTheBuildBake_RejectsGenuinePush()
        {
            // A driver whose build info carried no key (e.g. a build without the processor, or none
            // at all) keeps an empty key; the strict policy makes it reject even a signed push.
            var unbaked = UninitializedDriverKey();
            Assert.IsEmpty(unbaked, "a driver that never got a baked key has none");

            var status = PushEnvelope.TryOpen(unbaked, EditorSign(SampleReload()), m_PlayerNonce, 0, out _, out var recovered);
            Assert.AreEqual(PushVerifyStatus.NoKey, status);
            Assert.IsNull(recovered);
        }

        // A driver created but never initialized with a key — its PushPublicKey stays at its default (empty).
        private static string UninitializedDriverKey()
        {
            var go = new GameObject("e2e-unbaked-driver");
            try
            {
                return go.AddComponent<RuntimePipelineDriver>().PushPublicKey;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PushAfterEditorDomainReload_StillAccepted()
        {
            // A domain reload wipes the editor's per-session counter; it reseeds from UTC ticks, which
            // stays above whatever counter the player last accepted.
            Assert.AreEqual(PushVerifyStatus.Ok, m_PlayerVerifier.Verify(EditorSign(SampleReload()), out _));

            m_EditorCounter = 0; // editor domain reload lost its counter
            Assert.AreEqual(PushVerifyStatus.Ok, m_PlayerVerifier.Verify(EditorSign(SampleReload()), out _),
                "a counter reseeded after a domain reload must still climb above the player's last-seen value");
        }
    }
}
