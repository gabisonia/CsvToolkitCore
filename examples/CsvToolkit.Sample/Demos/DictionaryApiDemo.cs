using CsvToolkit.Core;

namespace CsvToolkit.Sample.Demos;

public static class DictionaryApiDemo
{
    public static void Run(string csvPath, CsvOptions options)
    {
        Console.WriteLine("\n[2] Dictionary API (TryReadDictionary)");
        using var stream = File.OpenRead(csvPath);
        using var reader = new CsvReader(stream, options);

        while (reader.TryReadDictionary(out var row))
        {
            Console.WriteLine($"{row["person_id"]} => {row["full_name"]} / {row["email"]}");
        }
    }
}
