using System.Linq.Expressions;
using System.Reflection;
using CsvToolkit.Core.TypeConversion;

namespace CsvToolkit.Core.Mapping;

public sealed class CsvMapBuilder<T>
{
    private readonly Dictionary<PropertyInfo, CsvFluentMemberConfig> _members = new();

    public CsvMemberMapBuilder<T, TProperty> Map<TProperty>(Expression<Func<T, TProperty>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
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

    public CsvMemberMapBuilder<T, TProperty> Converter(ICsvTypeConverter<TProperty> converter)
    {
        _config.Converter = new CsvTypeConverterAdapter<TProperty>(converter);
        return this;
    }
}

internal sealed class CsvFluentMemberConfig
{
    public string? Name { get; set; }

    public int? Index { get; set; }

    public bool Ignore { get; set; }

    public IUntypedCsvTypeConverter? Converter { get; set; }
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
