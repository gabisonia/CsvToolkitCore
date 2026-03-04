namespace CsvToolkit.Core.Mapping;

[AttributeUsage(AttributeTargets.Property)]
public sealed class CsvCultureAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
