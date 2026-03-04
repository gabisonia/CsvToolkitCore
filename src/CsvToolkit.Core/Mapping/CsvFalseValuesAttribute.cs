namespace CsvToolkit.Core.Mapping;

[AttributeUsage(AttributeTargets.Property)]
public sealed class CsvFalseValuesAttribute(params string[] values) : Attribute
{
    public string[] Values { get; } = values ?? [];
}
