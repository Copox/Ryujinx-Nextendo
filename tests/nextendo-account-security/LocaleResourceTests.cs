using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Ryujinx.AccountSecurity.Tests
{
    public class LocaleResourceTests
    {
        private static string ResourceDirectory => Path.Combine(AppContext.BaseDirectory, "LocaleValidation");

        public static IEnumerable<string> Languages()
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(ResourceDirectory, "Languages.json")));
            return document.RootElement.GetProperty("Languages").EnumerateObject().Select(p => p.Name).ToArray();
        }

        [Test]
        public void EveryLocaleDeclaresExactlyTheSupportedLanguages()
        {
            string[] supported = Languages().ToArray();
            foreach (string file in Directory.GetFiles(Path.Combine(ResourceDirectory, "Locales"), "*.json"))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
                foreach (JsonElement entry in document.RootElement.GetProperty("Locales").EnumerateArray())
                {
                    string[] keys = entry.GetProperty("Translations").EnumerateObject().Select(p => p.Name).ToArray();
                    Assert.That(keys, Is.EquivalentTo(supported), $"{Path.GetFileName(file)}: {entry.GetProperty("ID").GetString()}");
                }
            }
        }

        // Empty translations intentionally use the existing English fallback in LocaleManager.
        [TestCaseSource(nameof(Languages))]
        public void EveryLanguageResolvesEveryLocaleToNonemptyText(string language)
        {
            foreach (string file in Directory.GetFiles(Path.Combine(ResourceDirectory, "Locales"), "*.json"))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
                foreach (JsonElement entry in document.RootElement.GetProperty("Locales").EnumerateArray())
                {
                    JsonElement translations = entry.GetProperty("Translations");
                    string text = translations.TryGetProperty(language, out JsonElement value) ? value.GetString() : null;
                    if (string.IsNullOrEmpty(text)) text = translations.GetProperty("en_US").GetString();
                    Assert.That(text, Is.Not.Null.And.Not.Empty, $"{Path.GetFileName(file)}: {entry.GetProperty("ID").GetString()} ({language})");
                }
            }
        }
    }
}
