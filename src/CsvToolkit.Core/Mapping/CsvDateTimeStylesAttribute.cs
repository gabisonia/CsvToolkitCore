using System.Globalization;

namespace CsvToolkit.Core.Mapping;

[AttributeUsage(AttributeTargets.Property)]
public sealed class CsvDateTimeStylesAttribute(DateTimeStyles styles) : Attribute
{
    public DateTimeStyles Styles { get; } = styles;
}
