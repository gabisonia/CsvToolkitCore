using System.Globalization;

namespace CsvToolkit.Core.Mapping;

[AttributeUsage(AttributeTargets.Property)]
public sealed class CsvNumberStylesAttribute(NumberStyles styles) : Attribute
{
    public NumberStyles Styles { get; } = styles;
}
