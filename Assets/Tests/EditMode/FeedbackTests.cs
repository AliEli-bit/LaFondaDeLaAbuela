using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Feedback;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fonda.Tests
{
    public class FeedbackTests
    {
        GameObject root;
        readonly List<Object> assets = new List<Object>();
        readonly List<(FeedbackCue cue, AudioSource source)> played = new List<(FeedbackCue, AudioSource)>();

        [SetUp] public void SetUp() => root = new GameObject("TestRoot");

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            foreach (var a in assets) Object.DestroyImmediate(a);
            assets.Clear();
            played.Clear();
        }

        GameObject Child(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform);
            return go;
        }

        static string Field(FeedbackCue cue)
        {
            switch (cue)
            {
                case FeedbackCue.Plop: return "plop";
                case FeedbackCue.Bell: return "bell";
                case FeedbackCue.Wrong: return "wrong";
                case FeedbackCue.CoinsLeft: return "coinsLeft";
                default: return "coinBanked";
            }
        }

        // Every cue gets this many clips, except the ones in "silent".
        FeedbackSettings Settings(int clipsPerCue = 3, float volume = 0.6f, params FeedbackCue[] silent)
        {
            var settings = ScriptableObject.CreateInstance<FeedbackSettings>();
            assets.Add(settings);
            var so = new SerializedObject(settings);
            foreach (FeedbackCue cue in System.Enum.GetValues(typeof(FeedbackCue)))
            {
                var sound = so.FindProperty(Field(cue));
                sound.FindPropertyRelative("volume").floatValue = volume;
                var clips = sound.FindPropertyRelative("clips");
                clips.arraySize = silent.Contains(cue) ? 0 : clipsPerCue;
                for (int i = 0; i < clips.arraySize; i++)
                {
                    var clip = AudioClip.Create(cue + " " + i, 441, 1, 44100, false);
                    assets.Add(clip);
                    clips.GetArrayElementAtIndex(i).objectReferenceValue = clip;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return settings;
        }

        FeedbackManager Manager(FeedbackSettings settings, BarSeat[] seats = null, TipJar jar = null)
        {
            var manager = Child("Feedback").AddComponent<FeedbackManager>();
            manager.Bind(settings, new Tray[0], seats ?? new BarSeat[0], jar, null);
            manager.Played += (cue, source) => played.Add((cue, source));
            return manager;
        }

        // S3 + A3: "cada entrega rápida seguida sube el tono de la campanita".
        [Test]
        public void BellPitch_RisesWithTheCombo()
        {
            Assert.AreEqual(1f, FeedbackRule.BellPitch(0, 0.08f, 1.5f), 1e-4f, "slow delivery");
            Assert.AreEqual(1f, FeedbackRule.BellPitch(1, 0.08f, 1.5f), 1e-4f);
            Assert.AreEqual(1.08f, FeedbackRule.BellPitch(2, 0.08f, 1.5f), 1e-4f);
            Assert.AreEqual(1.24f, FeedbackRule.BellPitch(4, 0.08f, 1.5f), 1e-4f);
            Assert.AreEqual(1.5f, FeedbackRule.BellPitch(20, 0.08f, 1.5f), 1e-4f, "never above the max");
            Assert.AreEqual(1f, FeedbackRule.BellPitch(5, 0f, 1.5f), 1e-4f, "no step: always the base");
        }

        // A5: variants at random, never the same twice in a row.
        [Test]
        public void PickVariant_NeverRepeatsTheLastOne()
        {
            var seen = new HashSet<int>();
            for (float roll = 0f; roll < 1f; roll += 0.05f)
            {
                int pick = FeedbackRule.PickVariant(3, 1, roll);
                Assert.That(pick, Is.InRange(0, 2));
                Assert.AreNotEqual(1, pick, "roll " + roll);
                seen.Add(pick);
            }
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, seen);
            Assert.AreEqual(2, FeedbackRule.PickVariant(3, -1, 0.99f), "no last one: the whole range");
            Assert.AreEqual(0, FeedbackRule.PickVariant(1, 0, 0.5f), "a single clip repeats");
            Assert.AreEqual(-1, FeedbackRule.PickVariant(0, -1, 0.5f));
        }

        // S1 + A5: the cue's clip, in 3D, from where it happens, with the settings' volume.
        [Test]
        public void Play_UsesAClipOfTheCue_AtThePlace()
        {
            var settings = Settings(volume: 0.6f);
            var manager = Manager(settings);
            var position = new Vector3(0.34f, 0.9f, 0.4f);

            var source = manager.Play(FeedbackCue.Plop, position, 1.2f);

            Assert.IsNotNull(source);
            CollectionAssert.Contains(settings.For(FeedbackCue.Plop).Clips, source.clip);
            Assert.AreEqual(position, source.transform.position);
            Assert.AreEqual(1.2f, source.pitch, 1e-4f);
            Assert.AreEqual(0.6f, source.volume, 1e-4f);
            Assert.AreEqual(1f, source.spatialBlend, 1e-4f, "3D sound");
            Assert.AreEqual(settings.MinDistance, source.minDistance, 1e-4f);
            Assert.AreEqual(settings.MaxDistance, source.maxDistance, 1e-4f);
            Assert.AreEqual(1, played.Count);
            Assert.AreEqual(FeedbackCue.Plop, played[0].cue);
            Assert.AreSame(source, played[0].source);
        }

        // S5: a cue with no clip, or no settings, plays nothing and never fails.
        [Test]
        public void Play_WithoutClips_IsSilent()
        {
            var manager = Manager(Settings(silent: FeedbackCue.Bell));
            Assert.IsNull(manager.Play(FeedbackCue.Bell, Vector3.zero));
            Assert.IsNotNull(manager.Play(FeedbackCue.Wrong, Vector3.zero));

            var empty = Manager(null);
            Assert.IsNull(empty.Play(FeedbackCue.Plop, Vector3.zero));
            Assert.AreEqual(1, played.Count, "only the wrong sound played");
        }

        // A5: a small pool of voices, never one audio source per sound.
        [Test]
        public void Play_ReusesItsVoices()
        {
            var manager = Manager(Settings());
            for (int i = 0; i < 20; i++) manager.Play(FeedbackCue.Plop, Vector3.zero);
            Assert.That(manager.GetComponentsInChildren<AudioSource>(true).Length, Is.InRange(1, 6));
            Assert.AreEqual(20, played.Count);
        }

        // A5: the same clip never plays twice in a row.
        [Test]
        public void Play_NeverRepeatsTheSameClipTwiceInARow()
        {
            var manager = Manager(Settings());
            AudioClip last = null;
            for (int i = 0; i < 20; i++)
            {
                var clip = manager.Play(FeedbackCue.Plop, Vector3.zero).clip;
                Assert.AreNotSame(last, clip, "play " + i);
                last = clip;
            }
        }

        BarSeat Seat()
        {
            var seat = Child("Seat").AddComponent<BarSeat>();
            var spot = Child("CoinSpot").transform;
            spot.position = new Vector3(0.22f, 0.9f, 0.53f);
            var prefab = Child("Coin");
            prefab.AddComponent<Rigidbody>();
            prefab.AddComponent<XRGrabInteractable>();
            var so = new SerializedObject(seat);
            so.FindProperty("coinSpot").objectReferenceValue = spot;
            so.FindProperty("coinPrefab").objectReferenceValue = prefab.AddComponent<TipCoin>();
            so.ApplyModifiedPropertiesWithoutUndo();
            return seat;
        }

        List<TipCoin> LeaveTip(BarSeat seat, float waited)
        {
            var cycle = new CustomerCycle(25f, 3f);
            cycle.Sit();
            cycle.Tick(waited);
            if (!cycle.LeftAngry) cycle.Serve();
            var coins = seat.LeaveTip(cycle);
            foreach (var c in coins) c.transform.SetParent(root.transform);
            return coins;
        }

        // S4: coins sound when the customer leaves them, from the coin spot; an angry customer leaves none and no sound.
        [Test]
        public void Seat_LeavingCoins_PlaysTheCoins()
        {
            var seat = Seat();
            Manager(Settings(), new[] { seat });

            Assert.AreEqual(3, LeaveTip(seat, 0f).Count);
            Assert.AreEqual(1, played.Count, "one sound for the whole tip");
            Assert.AreEqual(FeedbackCue.CoinsLeft, played[0].cue);
            Assert.AreEqual(seat.CoinSpotPosition, played[0].source.transform.position);

            played.Clear();
            Assert.AreEqual(0, LeaveTip(seat, 30f).Count);
            Assert.AreEqual(0, played.Count, "no coins, no sound");
        }

        // S4: a coin into the jar sounds from the jar.
        [Test]
        public void Jar_BankingACoin_PlaysTheGlass()
        {
            var jar = Child("Jar").AddComponent<TipJar>();
            jar.transform.position = new Vector3(0.53f, 0.9f, 0.24f);
            Manager(Settings(), jar: jar);
            var coinGo = Child("Coin");
            coinGo.AddComponent<Rigidbody>();
            coinGo.AddComponent<XRGrabInteractable>();
            var coin = coinGo.AddComponent<TipCoin>();

            Assert.IsTrue(jar.Deposit(coin));

            Assert.AreEqual(1, played.Count);
            Assert.AreEqual(FeedbackCue.CoinBanked, played[0].cue);
            Assert.AreEqual(jar.transform.position, played[0].source.transform.position);
        }

        // S2 + A4: "Vibración (haptics) en controles al agarrar y soltar": release turns on, grab stays on.
        [Test]
        public void Rig_VibratesOnGrabAndOnRelease()
        {
            var rig = Child("Rig");
            var haptics = new List<SimpleHapticFeedback>();
            foreach (var hand in new[] { "Left", "Right" })
            {
                var go = new GameObject(hand + " Controller");
                go.transform.SetParent(rig.transform);
                var feedback = go.AddComponent<SimpleHapticFeedback>();
                feedback.playSelectEntered = true;
                feedback.playSelectExited = false;
                haptics.Add(feedback);
            }

            Assert.AreEqual(2, FondaSceneBuilder.EnableReleaseHaptics(rig));
            foreach (var h in haptics)
            {
                Assert.IsTrue(h.playSelectEntered, h.name + " grab");
                Assert.IsTrue(h.playSelectExited, h.name + " release");
            }
        }

        // S5 + S6 + L2: the chosen Kenney clips are in the project, one list per cue.
        [Test]
        public void DefaultClips_AreInTheProject()
        {
            var expected = new Dictionary<FeedbackCue, string>
            {
                { FeedbackCue.Plop, "drop_001,drop_002,drop_003" },
                { FeedbackCue.Bell, "impactBell_heavy_004" },
                { FeedbackCue.Wrong, "error_008" },
                { FeedbackCue.CoinsLeft, "handleCoins2" },
                { FeedbackCue.CoinBanked, "impactGlass_light_000,impactGlass_light_001,impactGlass_light_002,impactGlass_light_003,impactGlass_light_004" },
            };
            foreach (var pair in expected)
            {
                Assert.IsTrue(FondaCoreLoopBuilder.FeedbackClips.TryGetValue(pair.Key, out var paths), pair.Key.ToString());
                Assert.AreEqual(pair.Value, string.Join(",", paths.Select(System.IO.Path.GetFileNameWithoutExtension)));
                foreach (var path in paths)
                    Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AudioClip>(path), path);
            }
        }
    }
}
