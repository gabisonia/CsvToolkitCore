using CsvToolkit.Core;
using CsvToolkit.Sample.Models;

namespace CsvToolkit.Sample.Demos;

public static class AttributeRecordApiDemo
{
    public static void Run(string csvPath, CsvOptions options)
    {
        Console.WriteLine("\n[4] Record API with attributes (TryReadRecord + CsvColumn/CsvIndex/CsvIgnore)");
        using var stream = File.OpenRead(csvPath);
        using var reader = new CsvReader(stream, options);

        while (reader.TryReadRecord<AttributedPerson>(out var person))
        {
            Console.WriteLine(
                $"attr: {person.Id} {person.Name} | BirthDate={person.BirthDate:yyyy-MM-dd} | Age={person.Age}");
        }
    }
}
