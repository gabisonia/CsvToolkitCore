namespace CsvToolkit.Core.Mapping;

[AttributeUsage(AttributeTargets.Property)]
public sealed class CsvIndexAttribute(int index) : Attribute
{
    public int Index { get; } = index;
}
