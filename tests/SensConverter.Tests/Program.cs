using System.Globalization;

namespace SensConverter.Tests;

internal static class Program
{
    private static int Main()
    {
        // Fixed culture so number formatting assertions mean the same thing on every machine.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        Console.WriteLine("Sensitivity Converter - core tests");

        Harness harness = new();
        MathTests.Register(harness);
        CoreTests.Register(harness);
        AutoMeasureTests.Register(harness);
        VerticalEntryTests.Register(harness);
        StorageTests.Register(harness);

        return harness.Summarise();
    }
}
