using System.Globalization;

namespace CsvToolkit.Core.TypeConversion;

public sealed class CsvTypeConverterOptions
{
    private readonly List<string> _nullValues = [];
    private readonly List<string> _trueValues = [];
    private readonly List<string> _falseValues = [];
    private readonly List<string> _formats = [];

    public IReadOnlyList<string> NullValues => _nullValues;

    public IReadOnlyList<string> TrueValues => _trueValues;

    public IReadOnlyList<string> FalseValues => _falseValues;

    public IReadOnlyList<string> Formats => _formats;

    public NumberStyles? NumberStyles { get; set; }

    public DateTimeStyles DateTimeStyles { get; set; } = DateTimeStyles.None;

    public CultureInfo? CultureInfo { get; set; }

    public StringComparison ValueComparison { get; set; } = StringComparison.OrdinalIgnoreCase;

    public CsvTypeConverterOptions AddNullValues(params string[] values)
    {
        AddValues(_nullValues, values);
        return this;
    }

    public CsvTypeConverterOptions AddTrueValues(params string[] values)
    {
        AddValues(_trueValues, values);
        return this;
    }

    public CsvTypeConverterOptions AddFalseValues(params string[] values)
    {
        AddValues(_falseValues, values);
        return this;
    }

    public CsvTypeConverterOptions AddFormats(params string[] formats)
    {
        AddValues(_formats, formats);
        return this;
    }

    public bool IsNullValue(ReadOnlySpan<char> source)
    {
        return ContainsValue(_nullValues, source);
    }

    public bool IsTrueValue(ReadOnlySpan<char> source)
    {
        return ContainsValue(_trueValues, source);
    }

    public bool IsFalseValue(ReadOnlySpan<char> source)
    {
        return ContainsValue(_falseValues, source);
    }

    public CsvTypeConverterOptions Clone()
    {
        var clone = new CsvTypeConverterOptions
        {
            NumberStyles = NumberStyles,
            DateTimeStyles = DateTimeStyles,
            CultureInfo = CultureInfo,
            ValueComparison = ValueComparison
        };

        clone._nullValues.AddRange(_nullValues);
        clone._trueValues.AddRange(_trueValues);
        clone._falseValues.AddRange(_falseValues);
        clone._formats.AddRange(_formats);
        return clone;
    }

    private bool ContainsValue(IReadOnlyList<string> values, ReadOnlySpan<char> source)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (source.Equals(values[i].AsSpan(), ValueComparison))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddValues(List<string> target, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            target.Add(value);
        }
    }
}
