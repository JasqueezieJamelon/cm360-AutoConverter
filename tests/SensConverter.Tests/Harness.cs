using System.Globalization;

namespace SensConverter.Tests;

/// <summary>
/// A tiny test harness. Deliberately dependency-free so the suite runs with plain "dotnet run"
/// and needs nothing downloaded.
/// </summary>
internal sealed class Harness
{
    private readonly List<string> failures = new();

    public int Passed { get; private set; }

    public int Failed => failures.Count;

    public void Test(string name, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            body();
            Passed++;
            Console.WriteLine("  pass  " + name);
        }
        catch (Exception ex)
        {
            failures.Add(name + ": " + ex.Message);
            Console.WriteLine("  FAIL  " + name);
            Console.WriteLine("        " + ex.Message.Replace("\n", "\n        "));
        }
    }

    public void Section(string name) => Console.WriteLine(Environment.NewLine + name);

    public int Summarise()
    {
        Console.WriteLine();
        Console.WriteLine($"{Passed} passed, {Failed} failed.");

        if (failures.Count > 0)
        {
            Console.WriteLine();
            foreach (string failure in failures)
            {
                Console.WriteLine("  - " + failure);
            }
        }

        return failures.Count == 0 ? 0 : 1;
    }

    // ---- assertions ----

    public static void IsTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("expected true: " + message);
        }
    }

    public static void IsFalse(bool condition, string message)
    {
        if (condition)
        {
            throw new InvalidOperationException("expected false: " + message);
        }
    }

    public static void AreEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected <{expected}>, got <{actual}>");
        }
    }

    /// <summary>Floating point comparison with a relative tolerance.</summary>
    public static void AreClose(double expected, double actual, string message, double tolerance = 1e-9)
    {
        double scale = Math.Max(1.0, Math.Abs(expected));
        if (Math.Abs(expected - actual) > tolerance * scale)
        {
            throw new InvalidOperationException(
                $"{message}: expected {expected.ToString("R", CultureInfo.InvariantCulture)}, " +
                $"got {actual.ToString("R", CultureInfo.InvariantCulture)}");
        }
    }

    public static void Throws<TException>(Action body, string message)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            body();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"{message}: expected {typeof(TException).Name}, got {ex.GetType().Name}");
        }

        throw new InvalidOperationException($"{message}: expected {typeof(TException).Name}, nothing was thrown");
    }
}
