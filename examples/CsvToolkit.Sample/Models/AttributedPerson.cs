using CsvToolkit.Core.Mapping;

namespace CsvToolkit.Sample.Models;

public sealed class AttributedPerson
{
    [CsvIndex(0)] [CsvColumn("person_id")] public int Id { get; set; }

    [CsvColumn("full_name")] public string Name { get; set; } = string.Empty;

    [CsvColumn("email")] public string Email { get; set; } = string.Empty;

    [CsvColumn("age")] public int Age { get; set; }

    [CsvColumn("birth_date")] public DateOnly BirthDate { get; set; }

    [CsvIgnore] public string? IgnoredAtRuntime { get; set; }
}