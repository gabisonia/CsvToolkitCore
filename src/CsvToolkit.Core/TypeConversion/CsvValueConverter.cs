using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace CsvToolkit.Core.TypeConversion;

internal static class CsvValueConverter
{
    private const string RoundtripDateTimeFormat = "O";
    private static readonly ConcurrentDictionary<Type, BuiltInTypeInfo> BuiltInTypeInfoCache = new();
    private static readonly Type? DateOnlyType = Type.GetType("System.DateOnly");
    private static readonly MethodInfo? DateOnlyFromDateTimeMethod = DateOnlyType?.GetMethod(
        "FromDateTime",
        BindingFlags.Public | BindingFlags.Static,
        null,
        [typeof(DateTime)],
        null);
    private static readonly Type? TimeOnlyType = Type.GetType("System.TimeOnly");
    private static readonly MethodInfo? TimeOnlyFromDateTimeMethod = TimeOnlyType?.GetMethod(
        "FromDateTime",
        BindingFlags.Public | BindingFlags.Static,
        null,
        [typeof(DateTime)],
        null);

    public static bool TryConvert(
        ReadOnlySpan<char> source,
        Type targetType,
        CsvOptions options,
        IUntypedCsvTypeConverter? memberConverter,
        CsvTypeConverterOptions? memberConverterOptions,
        in CsvConverterContext context,
        out object? value)
    {
        var plan = CreateConversionPlan(targetType, options, memberConverter, memberConverterOptions);
        return TryConvert(source, plan, context, out value);
    }

    public static bool TryConvert(
        ReadOnlySpan<char> source,
        in CsvValueConversionPlan plan,
        in CsvConverterContext context,
        out object? value)
    {
        var typeInfo = GetBuiltInTypeInfo(plan.TargetType);
        if (plan.UseBuiltInPath)
        {
            return TryConvertBuiltInPath(source, typeInfo, context.CultureInfo, out value);
        }

        var converterOptions = plan.ConverterOptions;
        var culture = converterOptions?.CultureInfo ?? context.CultureInfo;
        var converterContext = CreateContextWithCulture(context, culture);

        if (typeInfo.AllowsNull && IsNullToken(source, converterOptions))
        {
            value = null;
            return true;
        }

        if (plan.Converter is not null)
        {
            return plan.Converter.TryParse(source, plan.TargetType, converterContext, out value);
        }

        if (TryConvertBuiltIn(source, typeInfo, culture, converterOptions, out value))
        {
            return true;
        }

        if (TryConvertFallback(source, typeInfo, culture, converterOptions, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    public static CsvValueConversionPlan CreateConversionPlan(
        Type targetType,
        CsvOptions options,
        IUntypedCsvTypeConverter? memberConverter,
        CsvTypeConverterOptions? memberConverterOptions)
    {
        var converterOptions = ResolveConverterOptions(targetType, options, memberConverterOptions);
        var converter = ResolveConverter(targetType, options, memberConverter);
        var useBuiltInPath = converter is null &&
                             converterOptions is null &&
                             CanUseBuiltInPathConversion(targetType);
        return new CsvValueConversionPlan(targetType, converter, converterOptions, useBuiltInPath);
    }

    public static bool CanUseBuiltInPathConversion(Type targetType)
    {
        return GetBuiltInTypeInfo(targetType).Kind != BuiltInTypeKind.Unsupported;
    }

    public static bool TryConvertBuiltInPath(
        ReadOnlySpan<char> source,
        Type targetType,
        CultureInfo culture,
        out object? value)
    {
        var typeInfo = GetBuiltInTypeInfo(targetType);
        return TryConvertBuiltInPath(source, typeInfo, culture, out value);
    }

    public static string FormatToString(
        object? value,
        Type valueType,
        CsvOptions options,
        IUntypedCsvTypeConverter? memberConverter,
        CsvTypeConverterOptions? memberConverterOptions,
        in CsvConverterContext context)
    {
        var plan = CreateConversionPlan(valueType, options, memberConverter, memberConverterOptions);
        return FormatToString(value, plan, context);
    }

    public static string FormatToString(
        object? value,
        in CsvValueConversionPlan plan,
        in CsvConverterContext context)
    {
        if (plan.UseBuiltInPath)
        {
            return FormatToStringBuiltInPath(value, context.CultureInfo);
        }

        var converterOptions = plan.ConverterOptions;
        var culture = converterOptions?.CultureInfo ?? context.CultureInfo;
        var converterContext = CreateContextWithCulture(context, culture);

        if (value is null)
        {
            if (converterOptions is { NullValues.Count: > 0 })
            {
                return converterOptions.NullValues[0];
            }

            return string.Empty;
        }

        if (plan.Converter is not null)
        {
            return plan.Converter.Format(value, plan.TargetType, converterContext);
        }

        if (TryFormatBuiltIn(value, culture, converterOptions, out var builtInFormatted))
        {
            return builtInFormatted;
        }

        if (value is IFormattable formattable)
        {
            var format = converterOptions is { Formats.Count: > 0 } ? converterOptions.Formats[0] : null;
            return formattable.ToString(format, culture) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }

    public static string FormatToStringBuiltInPath(object? value, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (TryFormatBuiltIn(value, culture, null, out var formatted))
        {
            return formatted;
        }

        return value.ToString() ?? string.Empty;
    }

    private static bool TryConvertBuiltInPath(
        ReadOnlySpan<char> source,
        in BuiltInTypeInfo typeInfo,
        CultureInfo culture,
        out object? value)
    {
        if (TryConvertBuiltIn(source, typeInfo, culture, null, out value))
        {
            return true;
        }

        if (TryConvertFallback(source, typeInfo, culture, null, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryConvertBuiltIn(
        ReadOnlySpan<char> source,
        in BuiltInTypeInfo typeInfo,
        CultureInfo culture,
        CsvTypeConverterOptions? converterOptions,
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
                if (converterOptions is not null)
                {
                    if (converterOptions.IsTrueValue(source))
                    {
                        value = true;
                        return true;
                    }

                    if (converterOptions.IsFalseValue(source))
                    {
                        value = false;
                        return true;
                    }
                }

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
                if (byte.TryParse(source, converterOptions?.NumberStyles ?? NumberStyles.Integer, culture,
                        out var byteValue))
                {
                    value = byteValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.SByte:
                if (sbyte.TryParse(source, converterOptions?.NumberStyles ?? NumberStyles.Integer, culture,
                        out var sbyteValue))
                {
                    value = sbyteValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Int16:
                if (short.TryParse(source, converterOptions?.NumberStyles ?? NumberStyles.Integer, culture,
                        out var shortValue))
                {
                    value = shortValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.UInt16:
                if (ushort.TryParse(source, converterOptions?.NumberStyles ?? NumberStyles.Integer, culture,
                        out var ushortValue))
                {
                    value = ushortValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Int32:
                if (int.TryParse(source, converterOptions?.NumberStyles ?? NumberStyles.Integer, culture,
                        out var intValue))
                {
                    value = intValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.UInt32:
                if (uint.TryParse(source, converterOptions?.NumberStyles ?? NumberStyles.Integer, culture,
                        out var uintValue))
                {
                    value = uintValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Int64:
                if (long.TryParse(source, converterOptions?.NumberStyles ?? NumberStyles.Integer, culture,
                        out var longValue))
                {
                    value = longValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.UInt64:
                if (ulong.TryParse(source, converterOptions?.NumberStyles ?? NumberStyles.Integer, culture,
                        out var ulongValue))
                {
                    value = ulongValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Single:
                if (float.TryParse(source,
                        converterOptions?.NumberStyles ?? (NumberStyles.Float | NumberStyles.AllowThousands), culture,
                        out var floatValue))
                {
                    value = floatValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Double:
                if (double.TryParse(source,
                        converterOptions?.NumberStyles ?? (NumberStyles.Float | NumberStyles.AllowThousands), culture,
                        out var doubleValue))
                {
                    value = doubleValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.Decimal:
                if (decimal.TryParse(source, converterOptions?.NumberStyles ?? NumberStyles.Number, culture,
                        out var decimalValue))
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
                if (TryParseDateTime(source, culture, converterOptions, out var dateTimeValue))
                {
                    value = dateTimeValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.DateOnly:
                if (TryConvertDateOnly(source, culture, converterOptions, out var dateOnlyValue))
                {
                    value = dateOnlyValue;
                    return true;
                }

                break;
            case BuiltInTypeKind.TimeOnly:
                if (TryConvertTimeOnly(source, culture, converterOptions, out var timeOnlyValue))
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
                if (Enum.TryParse(typeInfo.EffectiveType, source.ToString(), ignoreCase: true, out var enumValue))
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
        CsvTypeConverterOptions? converterOptions,
        out object? value)
    {
        try
        {
            if ((source.Length == 0 || IsNullToken(source, converterOptions)) && typeInfo.AllowsNull)
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

        if (DateOnlyType is not null && effectiveType == DateOnlyType)
        {
            return BuiltInTypeKind.DateOnly;
        }

        if (TimeOnlyType is not null && effectiveType == TimeOnlyType)
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

    private static bool TryConvertDateOnly(
        ReadOnlySpan<char> source,
        CultureInfo culture,
        CsvTypeConverterOptions? converterOptions,
        out object? value)
    {
        if (DateOnlyFromDateTimeMethod is null)
        {
            value = null;
            return false;
        }

        if (!TryParseDateTime(source, culture, converterOptions, out var dateTimeValue))
        {
            value = null;
            return false;
        }

        value = DateOnlyFromDateTimeMethod.Invoke(null, [dateTimeValue]);
        return value is not null;
    }

    private static bool TryConvertTimeOnly(
        ReadOnlySpan<char> source,
        CultureInfo culture,
        CsvTypeConverterOptions? converterOptions,
        out object? value)
    {
        if (TimeOnlyFromDateTimeMethod is null)
        {
            value = null;
            return false;
        }

        if (!TryParseDateTime(source, culture, converterOptions, out var dateTimeValue))
        {
            value = null;
            return false;
        }

        value = TimeOnlyFromDateTimeMethod.Invoke(null, [dateTimeValue]);
        return value is not null;
    }

    private static bool TryParseDateTime(
        ReadOnlySpan<char> source,
        CultureInfo culture,
        CsvTypeConverterOptions? converterOptions,
        out DateTime value)
    {
        if (converterOptions is { Formats.Count: > 0 })
        {
            for (var i = 0; i < converterOptions.Formats.Count; i++)
            {
                if (DateTime.TryParseExact(source, converterOptions.Formats[i], culture, converterOptions.DateTimeStyles,
                        out value))
                {
                    return true;
                }
            }
        }
        else if (ReferenceEquals(culture, CultureInfo.InvariantCulture) &&
                 DateTime.TryParseExact(source, RoundtripDateTimeFormat, CultureInfo.InvariantCulture,
                     DateTimeStyles.RoundtripKind, out value))
        {
            return true;
        }

        var styles = converterOptions?.DateTimeStyles ?? DateTimeStyles.None;
        return DateTime.TryParse(source, culture, styles, out value);
    }

    private static bool IsNullToken(ReadOnlySpan<char> source, CsvTypeConverterOptions? converterOptions)
    {
        if (source.Length == 0)
        {
            return true;
        }

        return converterOptions?.IsNullValue(source) == true;
    }

    private static CsvTypeConverterOptions? ResolveConverterOptions(
        Type targetType,
        CsvOptions options,
        CsvTypeConverterOptions? memberConverterOptions)
    {
        if (memberConverterOptions is not null)
        {
            return memberConverterOptions;
        }

        if (options.ConverterOptions.TryGet(targetType, out var typeOptions))
        {
            return typeOptions;
        }

        return null;
    }

    private static IUntypedCsvTypeConverter? ResolveConverter(
        Type targetType,
        CsvOptions options,
        IUntypedCsvTypeConverter? memberConverter)
    {
        if (memberConverter is not null)
        {
            return memberConverter;
        }

        if (options.Converters.TryGet(targetType, out var converter))
        {
            return converter;
        }

        return null;
    }

    private static CsvConverterContext CreateContextWithCulture(in CsvConverterContext context, CultureInfo culture)
    {
        if (ReferenceEquals(context.CultureInfo, culture))
        {
            return context;
        }

        return new CsvConverterContext(culture, context.RowIndex, context.FieldIndex, context.ColumnName);
    }

    private static bool TryFormatBuiltIn(
        object value,
        CultureInfo culture,
        CsvTypeConverterOptions? converterOptions,
        out string formatted)
    {
        if (value is bool boolValue)
        {
            if (boolValue && converterOptions is { TrueValues.Count: > 0 })
            {
                formatted = converterOptions.TrueValues[0];
                return true;
            }

            if (!boolValue && converterOptions is { FalseValues.Count: > 0 })
            {
                formatted = converterOptions.FalseValues[0];
                return true;
            }

            formatted = boolValue.ToString();
            return true;
        }

        if (value is IFormattable formattable)
        {
            var format = converterOptions is { Formats.Count: > 0 } ? converterOptions.Formats[0] : null;
            formatted = formattable.ToString(format, culture) ?? string.Empty;
            return true;
        }

        formatted = string.Empty;
        return false;
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

internal readonly record struct CsvValueConversionPlan(
    Type TargetType,
    IUntypedCsvTypeConverter? Converter,
    CsvTypeConverterOptions? ConverterOptions,
    bool UseBuiltInPath);
