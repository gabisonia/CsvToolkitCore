namespace CsvToolkit.Core.Mapping;

[AttributeUsage(AttributeTargets.Property)]
public sealed class CsvConstantAttribute(object? value) : Attribute
{
    public object? Value { get; } = value;
}
