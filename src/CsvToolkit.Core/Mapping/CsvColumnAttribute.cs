namespace CsvToolkit.Core.Mapping;

[AttributeUsage(AttributeTargets.Property)]
public sealed class CsvColumnAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
