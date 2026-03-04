using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using CsvToolkit.Core.TypeConversion;

namespace CsvToolkit.Core.Mapping;

public sealed class CsvMapBuilder<T>
{
    private readonly Dictionary<PropertyInfo, CsvFluentMemberConfig> _members = new();

    public CsvMemberMapBuilder<T, TProperty> Map<TProperty>(Expression<Func<T, TProperty>> selector)
    {
        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        var property = PropertyAccessor.FromExpression(selector);

        if (!_members.TryGetValue(property, out var config))
        {
            config = new CsvFluentMemberConfig();
            _members[property] = config;
        }

        return new CsvMemberMapBuilder<T, TProperty>(config);
    }

    internal IReadOnlyDictionary<PropertyInfo, CsvFluentMemberConfig> Build() => _members;
}

public sealed class CsvMemberMapBuilder<T, TProperty>
{
    private readonly CsvFluentMemberConfig _config;

    internal CsvMemberMapBuilder(CsvFluentMemberConfig config)
    {
        _config = config;
    }

    public CsvMemberMapBuilder<T, TProperty> Name(string name)
    {
        _config.Name = name;
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> NameIndex(int index)
    {
        _config.NameIndex = index;
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> Index(int index)
    {
        _config.Index = index;
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> Ignore()
    {
        _config.Ignore = true;
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> Optional()
    {
        _config.Optional = true;
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> Default(TProperty value)
    {
        _config.HasDefault = true;
        _config.DefaultValue = value;
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> Constant(TProperty value)
    {
        _config.HasConstant = true;
        _config.ConstantValue = value;
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> Validate(Func<TProperty, bool> validator, string? message = null)
    {
        if (validator is null)
        {
            throw new ArgumentNullException(nameof(validator));
        }

        _config.Validation = value =>
        {
            if (value is null)
            {
                if (default(TProperty) is null)
                {
                    return validator((TProperty)(object?)null!);
                }

                return false;
            }

            return value is TProperty typed && validator(typed);
        };
        _config.ValidationMessage = message;
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> Converter(ICsvTypeConverter<TProperty> converter)
    {
        _config.Converter = new CsvTypeConverterAdapter<TProperty>(converter);
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> NullValues(params string[] values)
    {
        EnsureConverterOptions().AddNullValues(values);
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> TrueValues(params string[] values)
    {
        EnsureConverterOptions().AddTrueValues(values);
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> FalseValues(params string[] values)
    {
        EnsureConverterOptions().AddFalseValues(values);
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> Formats(params string[] formats)
    {
        EnsureConverterOptions().AddFormats(formats);
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> NumberStyles(NumberStyles styles)
    {
        EnsureConverterOptions().NumberStyles = styles;
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> DateTimeStyles(DateTimeStyles styles)
    {
        EnsureConverterOptions().DateTimeStyles = styles;
        return this;
    }

    public CsvMemberMapBuilder<T, TProperty> Culture(CultureInfo culture)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        EnsureConverterOptions().CultureInfo = culture;
        return this;
    }

    private CsvTypeConverterOptions EnsureConverterOptions()
    {
        return _config.ConverterOptions ??= new CsvTypeConverterOptions();
    }
}

internal sealed class CsvFluentMemberConfig
{
    public string? Name { get; set; }

    public int? NameIndex { get; set; }

    public int? Index { get; set; }

    public bool Ignore { get; set; }

    public bool Optional { get; set; }

    public bool HasDefault { get; set; }

    public object? DefaultValue { get; set; }

    public bool HasConstant { get; set; }

    public object? ConstantValue { get; set; }

    public Func<object?, bool>? Validation { get; set; }

    public string? ValidationMessage { get; set; }

    public IUntypedCsvTypeConverter? Converter { get; set; }

    public CsvTypeConverterOptions? ConverterOptions { get; set; }
}

internal static class PropertyAccessor
{
    public static PropertyInfo FromExpression<T, TProperty>(Expression<Func<T, TProperty>> expression)
    {
        var memberExpression = expression.Body as MemberExpression;

        if (memberExpression is null && expression.Body is UnaryExpression unaryExpression)
        {
            memberExpression = unaryExpression.Operand as MemberExpression;
        }

        if (memberExpression?.Member is not PropertyInfo property)
        {
            throw new ArgumentException("Selector must target a property.", nameof(expression));
        }

        return property;
    }
}
