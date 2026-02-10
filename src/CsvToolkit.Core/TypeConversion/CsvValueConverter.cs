using System.Collections.Concurrent;
using System.Globalization;

namespace CsvToolkit.Core.TypeConversion;

internal static class CsvValueConverter
{
    private static readonly ConcurrentDictionary<Type, BuiltInTypeInfo> BuiltInTypeInfoCache = new();

    public static bool TryConvert(
        ReadOnlySpan<char> source,
        Type targetType,
        CsvOptions options,
        IUntypedCsvTypeConverter? memberConverter,
        in CsvConverterContext context,
        out object? value)
    {
        if (memberConverter is not null)
        {
            return memberConverter.TryParse(source, targetType, context, out value);
        }

        if (options.Converters.TryGet(targetType, out var converter))
        {
            return converter.TryParse(source, targetType, context, out value);
        }

        var typeInfo = GetBuiltInTypeInfo(targetType);

        if (TryConvertBuiltIn(source, typeInfo, context.CultureInfo, out value))
        {
            return true;
        }

        if (TryConvertFallback(source, typeInfo, context.CultureInfo, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    public static string FormatToString(
        object? value,
        Type valueType,
        CsvOptions options,
        IUntypedCsvTypeConverter? memberConverter,
        in CsvConverterContext context)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (memberConverter is not null)
        {
            return memberConverter.Format(value, valueType, context);
        }

        if (options.Converters.TryGet(valueType, out var converter))
        {
            return converter.Format(value, valueType, context);
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, context.CultureInfo) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }

    private static bool TryConvertBuiltIn(
        ReadOnlySpan<char> source,
        in BuiltInTypeInfo typeInfo,
        CultureInfo culture,
        out object? value)
    {
        if (source.Length == 0 && typeInfo.AllowsNull)
        {
            value = null;
            return true;
        }

        switch (typeInfo.Kind)
        {
            case BuiltInTypeKind.String:
                value = source.ToString();
                return true;
            case BuiltInTypeKind.Boolean:
                if (bool.TryParse(source, out var boolValue))
                {
                    value = boolValue;
                    return true;
                }

                if (source.Length == 1)
                {
                    if (source[0] == '1')
                    {
                        value = true;
                        return true;
                    }

                    if (source[0] == '0')
                    {
                        value = false;
                        return true;
                    }
                }

                break;
            case BuiltInTypeKind.Byte:
                if (byte.TryParse(source, NumberStyles.Integer, culture, out var byteValue))
                {
                    value = byteValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.SByte:
                if (sbyte.TryParse(source, NumberStyles.Integer, culture, out var sbyteValue))
                {
                    value = sbyteValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Int16:
                if (short.TryParse(source, NumberStyles.Integer, culture, out var shortValue))
                {
                    value = shortValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.UInt16:
                if (ushort.TryParse(source, NumberStyles.Integer, culture, out var ushortValue))
                {
                    value = ushortValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Int32:
                if (int.TryParse(source, NumberStyles.Integer, culture, out var intValue))
                {
                    value = intValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.UInt32:
                if (uint.TryParse(source, NumberStyles.Integer, culture, out var uintValue))
                {
                    value = uintValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Int64:
                if (long.TryParse(source, NumberStyles.Integer, culture, out var longValue))
                {
                    value = longValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.UInt64:
                if (ulong.TryParse(source, NumberStyles.Integer, culture, out var ulongValue))
                {
                    value = ulongValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Single:
                if (float.TryParse(source, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var floatValue))
                {
                    value = floatValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Double:
                if (double.TryParse(source, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var doubleValue))
                {
                    value = doubleValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Decimal:
                if (decimal.TryParse(source, NumberStyles.Number, culture, out var decimalValue))
                {
                    value = decimalValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Char:
                if (source.Length == 1)
                {
                    value = source[0];
                    return true;
                }

                break;
            case BuiltInTypeKind.DateTime:
                if (DateTime.TryParse(source, culture, DateTimeStyles.None, out var dateTimeValue))
                {
                    value = dateTimeValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.DateOnly:
                if (DateOnly.TryParse(source, culture, DateTimeStyles.None, out var dateOnlyValue))
                {
                    value = dateOnlyValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.TimeOnly:
                if (TimeOnly.TryParse(source, culture, DateTimeStyles.None, out var timeOnlyValue))
                {
                    value = timeOnlyValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Guid:
                if (Guid.TryParse(source, out var guidValue))
                {
                    value = guidValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Enum:
                if (Enum.TryParse(typeInfo.EffectiveType, source, ignoreCase: true, out var enumValue))
                {
                    value = enumValue;
                    return true;
                }

                break;
        }

        value = null;
        return false;
    }

    private static bool TryConvertFallback(
        ReadOnlySpan<char> source,
        in BuiltInTypeInfo typeInfo,
        CultureInfo culture,
        out object? value)
    {
        try
        {
            if (source.Length == 0 && typeInfo.AllowsNull)
            {
                value = null;
                return true;
            }

            var text = source.ToString();
            value = Convert.ChangeType(text, typeInfo.EffectiveType, culture);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private static BuiltInTypeInfo GetBuiltInTypeInfo(Type targetType)
    {
        return BuiltInTypeInfoCache.GetOrAdd(targetType, static type =>
        {
            var nullableType = Nullable.GetUnderlyingType(type);
            var effectiveType = nullableType ?? type;
            var allowsNull = nullableType is not null || !effectiveType.IsValueType;

            var kind = ResolveBuiltInTypeKind(effectiveType);
            return new BuiltInTypeInfo(effectiveType, allowsNull, kind);
        });
    }

    private static BuiltInTypeKind ResolveBuiltInTypeKind(Type effectiveType)
    {
        if (effectiveType == typeof(string))
        {
            return BuiltInTypeKind.String;
        }

        if (effectiveType == typeof(bool))
        {
            return BuiltInTypeKind.Boolean;
        }

        if (effectiveType == typeof(byte))
        {
            return BuiltInTypeKind.Byte;
        }

        if (effectiveType == typeof(sbyte))
        {
            return BuiltInTypeKind.SByte;
        }

        if (effectiveType == typeof(short))
        {
            return BuiltInTypeKind.Int16;
        }

        if (effectiveType == typeof(ushort))
        {
            return BuiltInTypeKind.UInt16;
        }

        if (effectiveType == typeof(int))
        {
            return BuiltInTypeKind.Int32;
        }

        if (effectiveType == typeof(uint))
        {
            return BuiltInTypeKind.UInt32;
        }

        if (effectiveType == typeof(long))
        {
            return BuiltInTypeKind.Int64;
        }

        if (effectiveType == typeof(ulong))
        {
            return BuiltInTypeKind.UInt64;
        }

        if (effectiveType == typeof(float))
        {
            return BuiltInTypeKind.Single;
        }

        if (effectiveType == typeof(double))
        {
            return BuiltInTypeKind.Double;
        }

        if (effectiveType == typeof(decimal))
        {
            return BuiltInTypeKind.Decimal;
        }

        if (effectiveType == typeof(char))
        {
            return BuiltInTypeKind.Char;
        }

        if (effectiveType == typeof(DateTime))
        {
            return BuiltInTypeKind.DateTime;
        }

        if (effectiveType == typeof(DateOnly))
        {
            return BuiltInTypeKind.DateOnly;
        }

        if (effectiveType == typeof(TimeOnly))
        {
            return BuiltInTypeKind.TimeOnly;
        }

        if (effectiveType == typeof(Guid))
        {
            return BuiltInTypeKind.Guid;
        }

        if (effectiveType.IsEnum)
        {
            return BuiltInTypeKind.Enum;
        }

        return BuiltInTypeKind.Unsupported;
    }

    private readonly record struct BuiltInTypeInfo(Type EffectiveType, bool AllowsNull, BuiltInTypeKind Kind);

    private enum BuiltInTypeKind
    {
        Unsupported,
        String,
        Boolean,
        Byte,
        SByte,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
        Single,
        Double,
        Decimal,
        Char,
        DateTime,
        DateOnly,
        TimeOnly,
        Guid,
        Enum
    }
}
