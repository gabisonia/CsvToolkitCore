namespace CsvToolkit.Core.Mapping;

[AttributeUsage(AttributeTargets.Property)]
public sealed class CsvValidateAttribute : Attribute
{
    public CsvValidateAttribute(string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("Method name cannot be null or whitespace.", nameof(methodName));
        }

        MethodName = methodName;
    }

    public CsvValidateAttribute(Type validatorType, string methodName) : this(methodName)
    {
        ValidatorType = validatorType ?? throw new ArgumentNullException(nameof(validatorType));
    }

    public Type? ValidatorType { get; }

    public string MethodName { get; }

    public string? Message { get; set; }
}
