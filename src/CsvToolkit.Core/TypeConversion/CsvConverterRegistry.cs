using System.Collections.Concurrent;

namespace CsvToolkit.Core.TypeConversion;

public sealed class CsvConverterRegistry
{
    private readonly ConcurrentDictionary<Type, IUntypedCsvTypeConverter> _converters = new();

    public void Register<T>(ICsvTypeConverter<T> converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        _converters[typeof(T)] = new CsvTypeConverterAdapter<T>(converter);
    }

    internal bool TryGet(Type type, out IUntypedCsvTypeConverter converter)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (_converters.TryGetValue(type, out converter!))
        {
            return true;
        }

        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType is not null && _converters.TryGetValue(nullableType, out converter!))
        {
            return true;
        }

        converter = default!;
        return false;
    }

    internal void CopyFrom(CsvConverterRegistry source)
    {
        foreach (var converter in source._converters)
        {
            _converters[converter.Key] = converter.Value;
        }
    }
}

internal interface IUntypedCsvTypeConverter
{
    bool TryParse(ReadOnlySpan<char> source, Type targetType, in CsvConverterContext context, out object? value);

    string Format(object? value, Type sourceType, in CsvConverterContext context);
}

internal sealed class CsvTypeConverterAdapter<T>(ICsvTypeConverter<T> converter) : IUntypedCsvTypeConverter
{
    public bool TryParse(ReadOnlySpan<char> source, Type targetType, in CsvConverterContext context, out object? value)
    {
        if (converter.TryParse(source, context, out var typed))
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    public string Format(object? value, Type sourceType, in CsvConverterContext context)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return converter.Format((T)value, context);
    }
}
