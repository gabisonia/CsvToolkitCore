using CsvToolkit.Core;

namespace CsvToolkit.Sample.Demos;

public static class DynamicApiDemo
{
    public static void Run(string csvPath, CsvOptions options)
    {
        Console.WriteLine("\n[3] Dynamic API (TryReadDynamic)");
        using var stream = File.OpenRead(csvPath);
        using var reader = new CsvReader(stream, options);

        while (reader.TryReadDynamic(out dynamic? row))
        {
            Console.WriteLine($"dynamic: {row.person_id} | {row.full_name} | {row.age}");
        }
    }
}
