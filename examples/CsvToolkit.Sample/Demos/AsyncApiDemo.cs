using CsvToolkit.Core;
using CsvToolkit.Sample.Models;

namespace CsvToolkit.Sample.Demos;

public static class AsyncApiDemo
{
    public static async Task RunAsync(string inputPath, string outputPath, CsvOptions options)
    {
        Console.WriteLine("\n[7] Async API (ReadRecordAsync + WriteRecordAsync)");
        var rows = new List<AttributedPerson>();

        await using (var input = File.OpenRead(inputPath))
        await using (var reader = new CsvReader(input, options))
        {
            AttributedPerson? row;
            while ((row = await reader.ReadRecordAsync<AttributedPerson>()) is not null)
            {
                rows.Add(row);
            }
        }

        await using (var output = File.Create(outputPath))
        await using (var writer = new CsvWriter(output, options))
        {
            await writer.WriteHeaderAsync<AttributedPerson>();
            foreach (var row in rows)
            {
                await writer.WriteRecordAsync(row);
            }

            await writer.FlushAsync();
        }

        Console.WriteLine($"Async copied {rows.Count} rows to: {outputPath}");
    }
}