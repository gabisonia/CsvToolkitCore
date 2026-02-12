using CsvToolkit.Core;
using CsvToolkit.Core.Mapping;
using CsvToolkit.Sample.Converters;
using CsvToolkit.Sample.Models;

namespace CsvToolkit.Sample.Demos;

public static class FluentMapApiDemo
{
    public static void Run(string csvPath, CsvOptions options)
    {
        Console.WriteLine("\n[5] Fluent mapper API (CsvMapRegistry)");
        var maps = new CsvMapRegistry();
        maps.Register<FluentEmployee>(map =>
        {
            map.Map(x => x.Id).Name("employee_id");
            map.Map(x => x.Name).Name("display_name");
            map.Map(x => x.StartDate).Name("started_on").Converter(new DateOnlyIsoConverter());
            map.Map(x => x.HourlyRate).Name("hourly_rate");
            map.Map(x => x.InternalNote).Ignore();
        });

        using var stream = File.OpenRead(csvPath);
        using var reader = new CsvReader(stream, options, maps);

        while (reader.TryReadRecord<FluentEmployee>(out var employee))
        {
            Console.WriteLine(
                $"fluent: {employee.Id} {employee.Name} | Start={employee.StartDate:yyyy-MM-dd} | Rate={employee.HourlyRate}");
        }
    }
}
