using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using CsvToolkit.Core.TypeConversion;

namespace CsvToolkit.Core.Mapping;

public sealed class CsvMapRegistry
{
    private readonly ConcurrentDictionary<Type, CsvTypeMap> _maps = new();

    public void Register<T>(Action<CsvMapBuilder<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new CsvMapBuilder<T>();
        configure(builder);
        _maps[typeof(T)] = CsvTypeMapFactory.Create(typeof(T), builder.Build());
    }

    internal CsvTypeMap GetOrCreate(Type type)
    {
        return _maps.GetOrAdd(type, static t => CsvTypeMapFactory.Create(t, null));
    }
}

internal static class CsvTypeMapFactory
{
    public static CsvTypeMap Create(Type type, IReadOnlyDictionary<PropertyInfo, CsvFluentMemberConfig>? fluentConfig)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var members = new List<CsvPropertyMap>(properties.Length);

        foreach (var property in properties)
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var map = new CsvPropertyMap
            {
                Property = property,
                Name = property.Name,
                Index = null,
                Ignore = false,
                Getter = BuildGetter(type, property),
                Setter = BuildSetter(type, property)
            };

            if (property.GetCustomAttribute<CsvIgnoreAttribute>() is not null)
            {
                map.Ignore = true;
            }

            if (property.GetCustomAttribute<CsvColumnAttribute>() is { } column)
            {
                map.Name = column.Name;
            }

            if (property.GetCustomAttribute<CsvIndexAttribute>() is { } index)
            {
                map.Index = index.Index;
            }

            if (fluentConfig is not null && fluentConfig.TryGetValue(property, out var overrideConfig))
            {
                if (overrideConfig.Name is not null)
                {
                    map.Name = overrideConfig.Name;
                }

                if (overrideConfig.Index.HasValue)
                {
                    map.Index = overrideConfig.Index;
                }

                if (overrideConfig.Ignore)
                {
                    map.Ignore = true;
                }

                if (overrideConfig.Converter is not null)
                {
                    map.Converter = overrideConfig.Converter;
                }
            }

            members.Add(map);
        }

        var ordered = members
            .OrderBy(static x => x.Index ?? int.MaxValue)
            .ThenBy(static x => x.Property.MetadataToken)
            .ToArray();

        return new CsvTypeMap(type, ordered);
    }

    private static Func<object, object?>? BuildGetter(Type recordType, PropertyInfo property)
    {
        if (!property.CanRead)
        {
            return null;
        }

        var instance = Expression.Parameter(typeof(object), "instance");
        var castInstance = Expression.Convert(instance, recordType);
        var propertyAccess = Expression.Property(castInstance, property);
        var box = Expression.Convert(propertyAccess, typeof(object));
        return Expression.Lambda<Func<object, object?>>(box, instance).Compile();
    }

    private static Action<object, object?>? BuildSetter(Type recordType, PropertyInfo property)
    {
        if (!property.CanWrite)
        {
            return null;
        }

        var instance = Expression.Parameter(typeof(object), "instance");
        var value = Expression.Parameter(typeof(object), "value");
        var castInstance = Expression.Convert(instance, recordType);
        var castValue = Expression.Convert(value, property.PropertyType);
        var assign = Expression.Assign(Expression.Property(castInstance, property), castValue);
        return Expression.Lambda<Action<object, object?>>(assign, instance, value).Compile();
    }
}

internal sealed class CsvTypeMap(Type recordType, CsvPropertyMap[] members)
{
    public Type RecordType { get; } = recordType;

    public CsvPropertyMap[] Members { get; } = members;
}

internal sealed class CsvPropertyMap
{
    public required PropertyInfo Property { get; init; }

    public required string Name { get; set; }

    public required int? Index { get; set; }

    public required bool Ignore { get; set; }

    public IUntypedCsvTypeConverter? Converter { get; set; }

    public Func<object, object?>? Getter { get; init; }

    public Action<object, object?>? Setter { get; init; }

    public Type PropertyType => Property.PropertyType;
}
