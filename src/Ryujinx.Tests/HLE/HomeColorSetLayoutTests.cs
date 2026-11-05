using NUnit.Framework;
using Ryujinx.Common.Configuration;
using Ryujinx.HLE;
using Ryujinx.HLE.HOS.SystemState;
using System;
using System.IO;
using System.Reflection;

namespace Ryujinx.Tests.HLE
{
    [NonParallelizable]
    public class HomeColorSetLayoutTests
    {
        private string _root;
        private static Type Settings => typeof(Switch).Assembly.GetType("Ryujinx.HLE.HOS.Services.Settings.ISystemSettingsServer", true);

        [SetUp]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "nextendo-home-layout-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void Cleanup()
        {
            Assert.That(Path.GetFullPath(_root), Does.StartWith(Path.Combine(Path.GetTempPath(), "nextendo-home-layout-test-")));
            Directory.Delete(_root, true);
        }

        [TestCase(ColorSet.BasicWhite)]
        [TestCase(ColorSet.BasicBlack)]
        public void SavedColorSetIsReadFromTheOrganizedLocation(ColorSet expected)
        {
            File.WriteAllText(Path.Combine(_root, "nextendo_color_set.txt"), ((int)expected).ToString());
            AppDataManager.Initialize(_root);
            object actual = Settings.GetMethod("ReadSavedColorSetFile", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(File.Exists(Path.Combine(_root, "nextendo_color_set.txt")), Is.False);
        }

        [Test]
        public void PriorHomeUseSurvivesTheNewFilesCreationTime()
        {
            string original = Path.Combine(_root, "nextendo_color_set.txt");
            File.WriteAllText(original, "0");
            File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddHours(-2));
            DateTime started = DateTime.UtcNow.AddMinutes(-1);
            AppDataManager.Initialize(_root);
            object actual = Settings.GetMethod("UsedBefore", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { _root, started });
            Assert.That(actual, Is.True);
        }
    }
}
