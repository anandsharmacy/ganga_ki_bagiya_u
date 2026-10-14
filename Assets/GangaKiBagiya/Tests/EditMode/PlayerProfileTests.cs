using System;
using System.IO;
using GangaKiBagiya;
using NUnit.Framework;

namespace GangaKiBagiya.Tests
{
    public sealed class PlayerProfileTests
    {
        private string directory;
        private PlayerProfileStore store;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "gkb-profile-" + Guid.NewGuid().ToString("N"));
            store = new PlayerProfileStore(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        private static PlayerProfile Valid(string name = "Alex") => new PlayerProfile
        {
            name = name, gender = PlayerGender.PreferNotToSay, age = 12, avatarIndex = 2, completed = true, createdUtc = "2026-10-14T00:00:00Z"
        };

        [Test]
        public void SavedProfileRoundTrips()
        {
            Assert.IsTrue(store.Save(Valid("आर्यन")));
            PlayerProfile loaded = store.Load(out bool damaged);
            Assert.IsFalse(damaged);
            Assert.AreEqual("आर्यन", loaded.name);
            Assert.AreEqual(PlayerGender.PreferNotToSay, loaded.gender);
            Assert.AreEqual(12, loaded.age);
            Assert.AreEqual(2, loaded.avatarIndex);
        }

        [Test]
        public void MissingFileMeansNoProfileAndNotDamaged()
        {
            Assert.IsNull(store.Load(out bool damaged));
            Assert.IsFalse(damaged);
        }

        [TestCase("{ not json")]
        [TestCase("{\"schemaVersion\":1,\"name\":\"\",\"completed\":true}")]
        [TestCase("{\"schemaVersion\":9,\"name\":\"Alex\",\"completed\":true}")]
        [TestCase("{\"schemaVersion\":1,\"name\":\"Alex\",\"age\":500,\"completed\":true}")]
        [TestCase("{\"schemaVersion\":1,\"name\":\"Alex\",\"completed\":false}")]
        public void CorruptOrInvalidFileIsMovedAsideAndReportedDamaged(string content)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(store.FilePath, content);
            Assert.IsNull(store.Load(out bool damaged));
            Assert.IsTrue(damaged);
            Assert.IsFalse(File.Exists(store.FilePath));
            Assert.IsTrue(File.Exists(store.FilePath + ".corrupt"));
            Assert.IsNull(store.Load(out damaged));
            Assert.IsFalse(damaged);
        }

        [Test]
        public void InvalidProfileIsNotSaved()
        {
            Assert.IsFalse(store.Save(Valid("")));
            Assert.IsFalse(File.Exists(store.FilePath));
        }

        [Test]
        public void OverwritingKeepsLatestProfile()
        {
            Assert.IsTrue(store.Save(Valid("Alex")));
            Assert.IsTrue(store.Save(Valid("Sam")));
            Assert.AreEqual("Sam", store.Load(out _).name);
        }

        [TestCase("", false)]
        [TestCase("   ", false)]
        [TestCase("A", false)]
        [TestCase("Al", true)]
        [TestCase("Alex", true)]
        [TestCase("  Mary   Jane ", true)]
        [TestCase("Ann-Marie O'Neil", true)]
        [TestCase("आर्यन शर्मा", true)]
        [TestCase("Alex99", false)]
        [TestCase("<b>x</b>", false)]
        [TestCase("12345", false)]
        [TestCase("ABCDEFGHIJKLMNOPQRSTU", false)]
        public void NameValidation(string input, bool ok)
        {
            Assert.AreEqual(ok, PlayerProfileValidation.TryName(input, out string cleaned, out string error));
            Assert.AreEqual(ok, error == null);
            if (input == "  Mary   Jane ")
                Assert.AreEqual("Mary Jane", cleaned);
        }

        [TestCase("", true, 0)]
        [TestCase("  ", true, 0)]
        [TestCase("5", true, 5)]
        [TestCase("120", true, 120)]
        [TestCase("12", true, 12)]
        [TestCase("4", false, 0)]
        [TestCase("0", false, 0)]
        [TestCase("121", false, 0)]
        [TestCase("-3", false, 0)]
        [TestCase("abc", false, 0)]
        [TestCase("12.5", false, 0)]
        [TestCase("1000", false, 0)]
        public void AgeValidation(string input, bool ok, int expected)
        {
            Assert.AreEqual(ok, PlayerProfileValidation.TryAge(input, out int age, out string error));
            Assert.AreEqual(expected, age);
            Assert.AreEqual(ok, error == null);
        }

        [Test]
        public void GenderIsOptional()
        {
            PlayerProfile profile = Valid();
            profile.gender = PlayerGender.Unspecified;
            Assert.IsTrue(profile.IsValid());
        }
    }
}
