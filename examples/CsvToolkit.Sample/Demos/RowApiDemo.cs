using CsvToolkit.Core;

namespace CsvToolkit.Sample.Demos;

public static class RowApiDemo
{
    public static void Run(string csvPath, CsvOptions options)
    {
        Console.WriteLine("\n[1] Row API (TryReadRow + spans)");
        using var stream = File.OpenRead(csvPath);
        using var reader = new CsvReader(stream, options);

        while (reader.TryReadRow(out var row))
        {
            var id = row.GetFieldSpan(0).ToString();
            var fullName = row.GetFieldString(1);
            Console.WriteLine($"Row {reader.RowIndex}: Id={id}, Name={fullName}");
        }
    }
}