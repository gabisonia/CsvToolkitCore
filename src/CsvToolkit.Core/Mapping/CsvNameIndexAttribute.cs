namespace CsvToolkit.Core.Mapping;

[AttributeUsage(AttributeTargets.Property)]
public sealed class CsvNameIndexAttribute(int index) : Attribute
{
    public int Index { get; } = index;
}
