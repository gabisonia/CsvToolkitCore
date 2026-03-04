namespace CsvToolkit.Core.Mapping;

[AttributeUsage(AttributeTargets.Property)]
public sealed class CsvNullValuesAttribute(params string[] values) : Attribute
{
    public string[] Values { get; } = values ?? [];
}
