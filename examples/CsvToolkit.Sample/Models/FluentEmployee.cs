namespace CsvToolkit.Sample.Models;

public sealed class FluentEmployee
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public decimal HourlyRate { get; set; }
    public string? InternalNote { get; set; }
}