using SensConverter.App.Gui;
using SensConverter.App.Input;
using SensConverter.Core;

namespace SensConverter.App;

internal static class Program
{
    private const string MutexName = @"Local\SensConverter.SingleInstance";

    private static Storage? storage;

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        if (args.Any(a => a is "--help" or "-h" or "/?"))
        {
            MessageBox.Show(
                "Sensitivity Converter\n\n" +
                "Usage: SensConverter.exe [--data <folder>]\n\n" +
                "--data   keep settings.json and games.json in this folder instead of next to the exe.",
                "Sensitivity Converter",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (args.Any(a => a is "--selftest"))
        {
            SelfTest.Run(ParseDataDirectory(args));
            return;
        }

        // One instance only: two copies would fight over the same hotkey and the same json files.
        using Mutex mutex = new(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Sensitivity Converter is already running.",
                "Already running",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            storage = Storage.CreateDefault(ParseDataDirectory(args));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(
                "Settings could not be stored anywhere:\n\n" + ex.Message,
                "Cannot start",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);

        AppSettings settings = storage.LoadSettings();
        GameLibrary library = storage.LoadLibrary();

        using GlobalInput input = new();

        if (!settings.Configured)
        {
            using SettingsForm setup = new(settings, firstRun: true);
            if (setup.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            settings = setup.Result;
            storage.SaveSettings(settings);
        }

        using MainForm main = new(storage, input, settings, library);
        Application.Run(main);
    }

    private static string? ParseDataDirectory(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--data" or "-d")
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static void ReportCrash(Exception? exception)
    {
        string text = exception?.ToString() ?? "Unknown error.";
        string? logPath = null;

        try
        {
            if (storage is not null)
            {
                logPath = Path.Combine(storage.DataDirectory, "crash.log");
                File.AppendAllText(logPath, $"{DateTimeOffset.Now:u}{Environment.NewLine}{text}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logPath = null;
        }

        string where = logPath is null ? string.Empty : $"\n\nWritten to {logPath}";
        MessageBox.Show(
            "Something went wrong:\n\n" + Shorten(text) + where,
            "Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static string Shorten(string text)
        => text.Length <= 600 ? text : text[..600] + "...";
}
