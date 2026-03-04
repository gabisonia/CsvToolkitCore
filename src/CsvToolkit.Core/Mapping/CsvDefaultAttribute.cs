namespace CsvToolkit.Core.Mapping;

[AttributeUsage(AttributeTargets.Property)]
public sealed class CsvDefaultAttribute(object? value) : Attribute
{
    public object? Value { get; } = value;
}
