using SensConverter.Core;

namespace SensConverter.Tests;

internal static class StorageTests
{
    public static void Register(Harness h)
    {
        ArgumentNullException.ThrowIfNull(h);

        h.Section("Storage");

        h.Test("settings survive a save and load", () => InTempFolder(dir =>
        {
            Storage storage = new(dir);
            AppSettings saved = new()
            {
                Dpi = 1600,
                TargetCm360 = 27.5,
                CalibrationHotkey = "Ctrl+F9",
                SpinsPerCalibration = 7,
                Configured = true,
            };
            storage.SaveSettings(saved);

            AppSettings loaded = new Storage(dir).LoadSettings();
            Harness.AreClose(1600, loaded.Dpi, "dpi");
            Harness.AreClose(27.5, loaded.TargetCm360, "target");
            Harness.AreEqual("Ctrl+F9", loaded.CalibrationHotkey, "hotkey");
            Harness.AreEqual(7, loaded.SpinsPerCalibration, "spins");
            Harness.IsTrue(loaded.Configured, "configured");
        }));

        h.Test("a calibrated game survives a save and load", () => InTempFolder(dir =>
        {
            Storage storage = new(dir);
            GameLibrary library = new();
            library.Upsert(GameEntry.FromCalibration(
                "Niche Shooter",
                @"C:\Games\niche\niche.exe",
                "abc123",
                1.0,
                new[] { 9400.0, 9450.0, 9497.0 },
                "hipfire only"));
            storage.SaveLibrary(library);

            GameLibrary loaded = new Storage(dir).LoadLibrary();
            Harness.AreEqual(1, loaded.Games.Count, "one game");

            GameEntry game = loaded.Games[0];
            Harness.AreEqual("Niche Shooter", game.Name, "name");
            Harness.AreEqual("hipfire only", game.Notes, "notes");
            Harness.AreEqual(3, game.Spins.Count, "spins kept");
            Harness.AreClose(9449, game.Counts360, "mean counts");
            Harness.AreClose(360.0 / 9449.0, game.Coefficient, "coefficient");
            Harness.IsTrue(game.IsCalibrated, "calibrated");

            AppSettings settings = new() { Dpi = 1600, TargetCm360 = 30 };
            Harness.AreClose(0.5, game.SensFor(settings), "suggested sens", 1e-4);
        }));

        h.Test("a game measured on both axes survives a save and load", () => InTempFolder(dir =>
        {
            Storage storage = new(dir);
            GameLibrary library = new();

            GameEntry saved = GameEntry.FromCalibration(
                "Both Axes",
                @"C:\Games\both\both.exe",
                "def456",
                1.5,
                new[] { 9449.0 },
                string.Empty,
                MeasurementMethod.Automatic);

            // Measured with the vertical slider on 2.0 while the horizontal one was on 1.5, and
            // the view pitched at three quarters the rate it yawed.
            saved.SetVertical(360.0 / 9449.0 * 1000 * 0.75, 1000, 2.0);
            library.Upsert(saved);
            storage.SaveLibrary(library);

            GameEntry loaded = new Storage(dir).LoadLibrary().Games[0];

            Harness.IsTrue(loaded.HasVertical, "vertical measurement kept");
            Harness.AreClose(saved.CoefficientY, loaded.CoefficientY, "vertical constant");
            Harness.AreClose(2.0, loaded.SensUsedDuringCalibrationY, "vertical sens used");
            // The stored ratio compares the two axes per unit of sens, so that it describes the
            // game itself rather than whatever the sliders happened to be on while measuring:
            // three quarters of the rate, but reached with a slider 2.0/1.5 higher.
            Harness.AreClose(0.75 * 1.5 / 2.0, loaded.MeasuredVerticalRatio, "ratio", 1e-6);
            Harness.AreEqual(MeasurementMethod.Automatic, loaded.Method, "method kept");

            AppSettings settings = new() { Dpi = 1600, TargetCm360 = 30, VerticalMultiplier = 1.0 };
            Harness.AreClose(
                saved.RoundedSensYFor(settings),
                loaded.RoundedSensYFor(settings),
                "same vertical recommendation after a round trip");
        }));

        h.Test("the library records the schema it was written with", () => InTempFolder(dir =>
        {
            Storage storage = new(dir);
            storage.SaveLibrary(new GameLibrary());

            string json = File.ReadAllText(storage.LibraryPath);
            Harness.IsTrue(
                json.Contains($"\"schemaVersion\": {GameLibrary.CurrentSchemaVersion}"),
                $"library schema {GameLibrary.CurrentSchemaVersion} written, got: {json}");

            storage.SaveSettings(new AppSettings());
            string settingsJson = File.ReadAllText(storage.SettingsPath);
            Harness.IsTrue(
                settingsJson.Contains($"\"schemaVersion\": {AppSettings.CurrentSchemaVersion}"),
                $"settings schema {AppSettings.CurrentSchemaVersion} written, got: {settingsJson}");
        }));

        h.Test("missing files give sensible defaults", () => InTempFolder(dir =>
        {
            Storage storage = new(dir);
            AppSettings settings = storage.LoadSettings();
            Harness.AreClose(800, settings.Dpi, "default dpi");
            Harness.AreClose(30, settings.TargetCm360, "default target");
            Harness.AreEqual("F8", settings.CalibrationHotkey, "default hotkey");
            Harness.IsFalse(settings.Configured, "not configured yet");
            Harness.AreEqual(0, storage.LoadLibrary().Games.Count, "empty library");
            Harness.AreEqual(0, storage.Warnings.Count, "no warnings");
        }));

        h.Test("a corrupt file is set aside, not deleted, and the user is told", () => InTempFolder(dir =>
        {
            File.WriteAllText(Path.Combine(dir, Storage.LibraryFileName), "{ this is not json");

            Storage storage = new(dir);
            GameLibrary library = storage.LoadLibrary();

            Harness.AreEqual(0, library.Games.Count, "started fresh");
            Harness.AreEqual(1, storage.Warnings.Count, "one warning");
            Harness.IsTrue(storage.Warnings[0].Contains("games.json", StringComparison.Ordinal), "names the file");
            Harness.IsTrue(
                Directory.GetFiles(dir, "games.bad-*.json").Length == 1,
                "the broken file is kept alongside");
        }));

        h.Test("hand-edited json with only spins is repaired on load", () => InTempFolder(dir =>
        {
            string json = """
            {
              "schemaVersion": 1,
              "games": [
                {
                  "name": "Typed By Hand",
                  "exePath": "C:\\x\\hand.exe",
                  "sensUsedDuringCalibration": 2,
                  "spins": [ 4700, 4750, 4725 ]
                }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(dir, Storage.LibraryFileName), json);

            GameEntry game = new Storage(dir).LoadLibrary().Games[0];
            Harness.AreClose(4725, game.Counts360, "counts filled in from the spins");
            Harness.AreClose(360.0 / (4725.0 * 2.0), game.Coefficient, "coefficient derived");
            Harness.IsTrue(game.Id.Length > 0, "an id was generated");
        }));

        h.Test("hand-edited nonsense in settings is clamped", () => InTempFolder(dir =>
        {
            File.WriteAllText(
                Path.Combine(dir, Storage.SettingsFileName),
                """{ "dpi": 0, "targetCm360": -5, "calibrationHotkey": "Banana", "spinsPerCalibration": 500 }""");

            AppSettings settings = new Storage(dir).LoadSettings();
            Harness.AreClose(800, settings.Dpi, "dpi reset");
            Harness.AreClose(30, settings.TargetCm360, "target reset");
            Harness.AreEqual("F8", settings.CalibrationHotkey, "hotkey reset");
            Harness.AreEqual(20, settings.SpinsPerCalibration, "spins clamped");
        }));

        h.Test("saving leaves no temp files behind", () => InTempFolder(dir =>
        {
            Storage storage = new(dir);
            storage.SaveSettings(new AppSettings());
            storage.SaveLibrary(new GameLibrary());
            Harness.AreEqual(0, Directory.GetFiles(dir, "*.tmp").Length, "no .tmp");
            Harness.AreEqual(2, Directory.GetFiles(dir, "*.json").Length, "two json files");
        }));

        h.Test("the json is readable and camelCase", () => InTempFolder(dir =>
        {
            Storage storage = new(dir);
            storage.SaveSettings(new AppSettings { Dpi = 1600 });
            string text = File.ReadAllText(storage.SettingsPath);
            Harness.IsTrue(text.Contains("\"dpi\": 1600", StringComparison.Ordinal), "camelCase and indented");
        }));

        h.Section("GameLibrary");

        h.Test("a game is found by its exe hash whatever the letter case", () =>
        {
            GameLibrary library = new();
            library.Upsert(Entry("A", "AABBCC"));
            Harness.IsTrue(library.FindByHash("aabbcc") is not null, "case-insensitive hash match");
            Harness.IsTrue(library.FindByHash("ffffff") is null, "unknown hash");
            Harness.IsTrue(library.FindByHash(null) is null, "null hash");
        });

        h.Test("a game is found by path, case-insensitively", () =>
        {
            GameLibrary library = new();
            GameEntry entry = Entry("A", "AABBCC");
            entry.ExePath = @"C:\Games\A\a.exe";
            library.Upsert(entry);
            Harness.IsTrue(library.FindByPath(@"c:\games\a\A.EXE") is not null, "path match");
        });

        h.Test("recalibrating replaces the entry instead of adding one", () =>
        {
            GameLibrary library = new();
            GameEntry first = Entry("A", "AABBCC");
            library.Upsert(first);

            GameEntry second = GameEntry.FromCalibration("A", first.ExePath, first.ExeHash, 1.0, new[] { 5000.0 });
            second.Id = first.Id;
            library.Upsert(second);

            Harness.AreEqual(1, library.Games.Count, "still one game");
            Harness.AreClose(5000, library.Games[0].Counts360, "new measurement won");
        });

        h.Test("removing works by id and ignores unknown ids", () =>
        {
            GameLibrary library = new();
            GameEntry entry = Entry("A", "AABBCC");
            library.Upsert(entry);
            Harness.IsFalse(library.Remove("nope"), "unknown id");
            Harness.IsTrue(library.Remove(entry.Id), "known id");
            Harness.AreEqual(0, library.Games.Count, "empty");
        });

        h.Test("the list sorts by name, ignoring case", () =>
        {
            GameLibrary library = new();
            library.Upsert(Entry("zebra", "1"));
            library.Upsert(Entry("Apple", "2"));
            library.Upsert(Entry("mango", "3"));
            library.SortByName();
            Harness.AreEqual("Apple", library.Games[0].Name, "first");
            Harness.AreEqual("mango", library.Games[1].Name, "second");
            Harness.AreEqual("zebra", library.Games[2].Name, "third");
        });

        h.Section("GameEntry");

        h.Test("calibration with no usable spins is refused", () =>
            Harness.Throws<ArgumentException>(
                () => GameEntry.FromCalibration("X", "x.exe", "h", 1.0, Array.Empty<double>()),
                "no spins"));

        h.Test("an uncalibrated entry reports itself as such", () =>
        {
            GameEntry entry = new() { Name = "X" };
            Harness.IsFalse(entry.IsCalibrated, "no coefficient yet");
        });
    }

    private static GameEntry Entry(string name, string hash)
    {
        GameEntry entry = GameEntry.FromCalibration(name, $@"C:\Games\{name}\{name}.exe", hash, 1.0, new[] { 9449.0 });
        return entry;
    }

    private static void InTempFolder(Action<string> body)
    {
        string dir = Path.Combine(Path.GetTempPath(), "sensconv-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);

        try
        {
            body(dir);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp folder is not worth failing a test over.
            }
        }
    }
}
