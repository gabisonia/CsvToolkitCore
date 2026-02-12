using System.Globalization;
using CsvToolkit.Core;
using CsvToolkit.Sample.Converters;

namespace CsvToolkit.Sample.Infrastructure;

public static class CsvOptionsFactory
{
    public static CsvOptions Create()
    {
        var options = new CsvOptions
        {
            HasHeader = true,
            TrimOptions = CsvTrimOptions.Trim,
            ReadMode = CsvReadMode.Strict,
            CultureInfo = CultureInfo.InvariantCulture,
            NewLine = "\n"
        };

        options.Converters.Register(new DateOnlyIsoConverter());
        options.BadDataFound = context =>
        {
            Console.WriteLine(
                $"[BadData] Row={context.RowIndex} Field={context.FieldIndex} Message={context.Message}");
        };

        return options;
    }
}