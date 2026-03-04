using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using CsvToolkit.Core.TypeConversion;

namespace CsvToolkit.Core.Mapping;

public sealed class CsvMapRegistry
{
    private readonly ConcurrentDictionary<Type, CsvTypeMap> _maps = new();

    public void Register<T>(Action<CsvMapBuilder<T>> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

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
                NameIndex = null,
                Index = null,
                Ignore = false,
                Optional = false,
                HasDefault = false,
                DefaultValue = null,
                HasConstant = false,
                ConstantValue = null,
                Validation = null,
                ValidationMessage = null,
                ConverterOptions = null,
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

            ApplyAttributeConfiguration(type, property, map);

            if (fluentConfig is not null && fluentConfig.TryGetValue(property, out var overrideConfig))
            {
                if (overrideConfig.Name is not null)
                {
                    map.Name = overrideConfig.Name;
                }

                if (overrideConfig.NameIndex.HasValue)
                {
                    map.NameIndex = overrideConfig.NameIndex;
                }

                if (overrideConfig.Index.HasValue)
                {
                    map.Index = overrideConfig.Index;
                }

                if (overrideConfig.Ignore)
                {
                    map.Ignore = true;
                }

                if (overrideConfig.Optional)
                {
                    map.Optional = true;
                }

                if (overrideConfig.HasDefault)
                {
                    map.HasDefault = true;
                    map.DefaultValue = overrideConfig.DefaultValue;
                }

                if (overrideConfig.HasConstant)
                {
                    map.HasConstant = true;
                    map.ConstantValue = overrideConfig.ConstantValue;
                }

                if (overrideConfig.Validation is not null)
                {
                    map.Validation = overrideConfig.Validation;
                    map.ValidationMessage = overrideConfig.ValidationMessage;
                }

                if (overrideConfig.Converter is not null)
                {
                    map.Converter = overrideConfig.Converter;
                }

                if (overrideConfig.ConverterOptions is not null)
                {
                    map.ConverterOptions = overrideConfig.ConverterOptions.Clone();
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

    private static void ApplyAttributeConfiguration(Type recordType, PropertyInfo property, CsvPropertyMap map)
    {
        if (property.GetCustomAttribute<CsvNameIndexAttribute>() is { } nameIndex)
        {
            map.NameIndex = nameIndex.Index;
        }

        if (property.GetCustomAttribute<CsvOptionalAttribute>() is not null)
        {
            map.Optional = true;
        }

        if (property.GetCustomAttribute<CsvDefaultAttribute>() is { } defaultAttribute)
        {
            map.HasDefault = true;
            map.DefaultValue = ConvertAttributeValue(property, defaultAttribute.Value, nameof(CsvDefaultAttribute));
        }

        if (property.GetCustomAttribute<CsvConstantAttribute>() is { } constantAttribute)
        {
            map.HasConstant = true;
            map.ConstantValue = ConvertAttributeValue(property, constantAttribute.Value, nameof(CsvConstantAttribute));
        }

        if (property.GetCustomAttribute<CsvValidateAttribute>() is { } validateAttribute)
        {
            map.Validation = BuildValidationDelegate(recordType, property, validateAttribute);
            map.ValidationMessage = validateAttribute.Message;
        }

        if (property.GetCustomAttribute<CsvNullValuesAttribute>() is { } nullValuesAttribute)
        {
            EnsureConverterOptions(map).AddNullValues(nullValuesAttribute.Values);
        }

        if (property.GetCustomAttribute<CsvTrueValuesAttribute>() is { } trueValuesAttribute)
        {
            EnsureConverterOptions(map).AddTrueValues(trueValuesAttribute.Values);
        }

        if (property.GetCustomAttribute<CsvFalseValuesAttribute>() is { } falseValuesAttribute)
        {
            EnsureConverterOptions(map).AddFalseValues(falseValuesAttribute.Values);
        }

        if (property.GetCustomAttribute<CsvFormatsAttribute>() is { } formatsAttribute)
        {
            EnsureConverterOptions(map).AddFormats(formatsAttribute.Formats);
        }

        if (property.GetCustomAttribute<CsvNumberStylesAttribute>() is { } numberStylesAttribute)
        {
            EnsureConverterOptions(map).NumberStyles = numberStylesAttribute.Styles;
        }

        if (property.GetCustomAttribute<CsvDateTimeStylesAttribute>() is { } dateTimeStylesAttribute)
        {
            EnsureConverterOptions(map).DateTimeStyles = dateTimeStylesAttribute.Styles;
        }

        if (property.GetCustomAttribute<CsvCultureAttribute>() is { } cultureAttribute)
        {
            try
            {
                EnsureConverterOptions(map).CultureInfo = CultureInfo.GetCultureInfo(cultureAttribute.Name);
            }
            catch (CultureNotFoundException ex)
            {
                throw new InvalidOperationException(
                    $"Invalid culture '{cultureAttribute.Name}' configured for member '{property.Name}'.", ex);
            }
        }
    }

    private static CsvTypeConverterOptions EnsureConverterOptions(CsvPropertyMap map)
    {
        return map.ConverterOptions ??= new CsvTypeConverterOptions();
    }

    private static object? ConvertAttributeValue(PropertyInfo property, object? value, string attributeName)
    {
        var propertyType = property.PropertyType;
        if (value is null)
        {
            if (IsNullableType(propertyType))
            {
                return null;
            }

            throw new InvalidOperationException(
                $"{attributeName} on member '{property.Name}' cannot use null for non-nullable type '{propertyType.Name}'.");
        }

        if (propertyType.IsInstanceOfType(value))
        {
            return value;
        }

        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (TryConvertValue(value, propertyType, out var converted))
        {
            return converted;
        }

        throw new InvalidOperationException(
            $"{attributeName} value on member '{property.Name}' is not assignable to '{propertyType.Name}'.");
    }

    private static bool TryConvertValue(object value, Type targetType, out object? converted)
    {
        converted = null;
        var nonNullableTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (nonNullableTargetType.IsEnum)
        {
            if (value is string enumText &&
                Enum.TryParse(nonNullableTargetType, enumText, ignoreCase: true, out var enumValue))
            {
                converted = enumValue;
                return true;
            }

            if (IsNumericType(value.GetType()))
            {
                try
                {
                    var enumUnderlyingType = Enum.GetUnderlyingType(nonNullableTargetType);
                    var numericValue = Convert.ChangeType(value, enumUnderlyingType, CultureInfo.InvariantCulture);
                    converted = Enum.ToObject(nonNullableTargetType, numericValue!);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(nonNullableTargetType))
        {
            try
            {
                converted = Convert.ChangeType(value, nonNullableTargetType, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                // fall through
            }
        }

        if (value is string text)
        {
            var options = new CsvOptions { CultureInfo = CultureInfo.InvariantCulture };
            var context = new CsvConverterContext(CultureInfo.InvariantCulture, 0, -1, null);
            if (CsvValueConverter.TryConvert(text.AsSpan(), targetType, options, null, null, context, out converted))
            {
                return true;
            }
        }

        return false;
    }

    private static Func<object?, bool> BuildValidationDelegate(
        Type recordType,
        PropertyInfo property,
        CsvValidateAttribute validateAttribute)
    {
        var validatorType = validateAttribute.ValidatorType ?? recordType;
        var method = validatorType.GetMethod(
            validateAttribute.MethodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        if (method is null)
        {
            throw new InvalidOperationException(
                $"Validation method '{validateAttribute.MethodName}' was not found on type '{validatorType.Name}' for member '{property.Name}'.");
        }

        if (method.ReturnType != typeof(bool))
        {
            throw new InvalidOperationException(
                $"Validation method '{validatorType.Name}.{method.Name}' must return bool.");
        }

        var parameters = method.GetParameters();
        if (parameters.Length != 1)
        {
            throw new InvalidOperationException(
                $"Validation method '{validatorType.Name}.{method.Name}' must have exactly one parameter.");
        }

        var parameterType = parameters[0].ParameterType;
        return value =>
        {
            if (!TryConvertValidationArgument(value, parameterType, out var converted))
            {
                return false;
            }

            var result = method.Invoke(null, new[] { converted });
            return result is bool valid && valid;
        };
    }

    private static bool TryConvertValidationArgument(object? value, Type parameterType, out object? converted)
    {
        converted = null;

        if (value is null)
        {
            return IsNullableType(parameterType);
        }

        if (parameterType.IsInstanceOfType(value))
        {
            converted = value;
            return true;
        }

        return TryConvertValue(value, parameterType, out converted);
    }

    private static bool IsNullableType(Type type)
    {
        return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
    }

    private static bool IsNumericType(Type type)
    {
        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                return true;
            default:
                return false;
        }
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

    public required int? NameIndex { get; set; }

    public required int? Index { get; set; }

    public required bool Ignore { get; set; }

    public required bool Optional { get; set; }

    public required bool HasDefault { get; set; }

    public required object? DefaultValue { get; set; }

    public required bool HasConstant { get; set; }

    public required object? ConstantValue { get; set; }

    public required Func<object?, bool>? Validation { get; set; }

    public required string? ValidationMessage { get; set; }

    public IUntypedCsvTypeConverter? Converter { get; set; }

    public CsvTypeConverterOptions? ConverterOptions { get; set; }

    public Func<object, object?>? Getter { get; init; }

    public Action<object, object?>? Setter { get; init; }

    public Type PropertyType => Property.PropertyType;
}
