using System.Globalization;
using CsvToolkit.Core;
using CsvToolkit.Core.TypeConversion;

namespace CsvToolkit.Sample.Converters;

public sealed class DateOnlyIsoConverter : ICsvTypeConverter<DateOnly>
{
    private const string IsoPattern = "yyyy-MM-dd";

    public bool TryParse(ReadOnlySpan<char> source, in CsvConverterContext context, out DateOnly value)
    {
        return DateOnly.TryParseExact(source, IsoPattern, context.CultureInfo, DateTimeStyles.None, out value);
    }

    public string Format(DateOnly value, in CsvConverterContext context)
    {
        return value.ToString(IsoPattern, context.CultureInfo);
    }
}
