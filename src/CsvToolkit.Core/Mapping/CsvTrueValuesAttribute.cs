namespace CsvToolkit.Core.Mapping;

[AttributeUsage(AttributeTargets.Property)]
public sealed class CsvTrueValuesAttribute(params string[] values) : Attribute
{
    public string[] Values { get; } = values ?? [];
}
