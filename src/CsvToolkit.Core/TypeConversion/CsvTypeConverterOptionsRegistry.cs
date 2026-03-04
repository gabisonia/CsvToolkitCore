using System.Collections.Concurrent;

namespace CsvToolkit.Core.TypeConversion;

public sealed class CsvTypeConverterOptionsRegistry
{
    private readonly ConcurrentDictionary<Type, CsvTypeConverterOptions> _options = new();

    public CsvTypeConverterOptions GetOrCreate<T>()
    {
        return GetOrCreate(typeof(T));
    }

    public CsvTypeConverterOptions GetOrCreate(Type type)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        return _options.GetOrAdd(type, static _ => new CsvTypeConverterOptions());
    }

    public void Configure<T>(Action<CsvTypeConverterOptions> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        configure(GetOrCreate<T>());
    }

    internal bool TryGet(Type type, out CsvTypeConverterOptions options)
    {
        if (_options.TryGetValue(type, out options!))
        {
            return true;
        }

        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType is not null && _options.TryGetValue(nullableType, out options!))
        {
            return true;
        }

        options = default!;
        return false;
    }

    internal void CopyFrom(CsvTypeConverterOptionsRegistry source)
    {
        foreach (var option in source._options)
        {
            _options[option.Key] = option.Value.Clone();
        }
    }
}
