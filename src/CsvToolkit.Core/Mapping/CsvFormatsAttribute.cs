namespace CsvToolkit.Core.Mapping;

[AttributeUsage(AttributeTargets.Property)]
public sealed class CsvFormatsAttribute(params string[] formats) : Attribute
{
    public string[] Formats { get; } = formats ?? [];
}
