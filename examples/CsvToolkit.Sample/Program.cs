using System.Globalization;
using CsvToolkit.Core;
using CsvToolkit.Core.Mapping;
using CsvToolkit.Core.TypeConversion;

var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
var peoplePath = Path.Combine(dataDirectory, "people.csv");
var employeesPath = Path.Combine(dataDirectory, "employees_fluent.csv");
var outputDirectory = Path.Combine(AppContext.BaseDirectory, "output");

Directory.CreateDirectory(outputDirectory);

Console.WriteLine("=== CsvToolkit.Core Sample ===");
Console.WriteLine($"Data directory: {dataDirectory}");

var options = CreateOptions();

RunRowApi(peoplePath, options);
RunDictionaryApi(peoplePath, options);
RunDynamicApi(peoplePath, options);
RunAttributeRecordApi(peoplePath, options);
RunFluentMapApi(employeesPath, options);
RunWriteApi(Path.Combine(outputDirectory, "people-export.csv"), options);
await RunAsyncApi(peoplePath, Path.Combine(outputDirectory, "people-export-async.csv"), options);

Console.WriteLine("=== Sample Completed ===");

static CsvOptions CreateOptions()
{
    var options = new CsvOptions
    {
        HasHeader = true,
        TrimOptions = CsvTrimOptions.Trim,
        ReadMode = CsvReadMode.Strict,
        CultureInfo = CultureInfo.InvariantCulture,
        NewLine = "\n"
    };

    options.Converters.Register(new DateOnlyIsoConverter());
    options.BadDataFound = context =>
    {
        Console.WriteLine($"[BadData] Row={context.RowIndex} Field={context.FieldIndex} Message={context.Message}");
    };

    return options;
}

static void RunRowApi(string csvPath, CsvOptions options)
{
    Console.WriteLine("\n[1] Row API (TryReadRow + spans)");
    using var stream = File.OpenRead(csvPath);
    using var reader = new CsvReader(stream, options);

    while (reader.TryReadRow(out var row))
    {
        var id = row.GetFieldSpan(0).ToString();
        var fullName = row.GetFieldString(1);
        Console.WriteLine($"Row {reader.RowIndex}: Id={id}, Name={fullName}");
    }
}

static void RunDictionaryApi(string csvPath, CsvOptions options)
{
    Console.WriteLine("\n[2] Dictionary API (TryReadDictionary)");
    using var stream = File.OpenRead(csvPath);
    using var reader = new CsvReader(stream, options);

    while (reader.TryReadDictionary(out var row))
    {
        Console.WriteLine($"{row["person_id"]} => {row["full_name"]} / {row["email"]}");
    }
}

static void RunDynamicApi(string csvPath, CsvOptions options)
{
    Console.WriteLine("\n[3] Dynamic API (TryReadDynamic)");
    using var stream = File.OpenRead(csvPath);
    using var reader = new CsvReader(stream, options);

    while (reader.TryReadDynamic(out dynamic? row))
    {
        Console.WriteLine($"dynamic: {row.person_id} | {row.full_name} | {row.age}");
    }
}

static void RunAttributeRecordApi(string csvPath, CsvOptions options)
{
    Console.WriteLine("\n[4] Record API with attributes (TryReadRecord + CsvColumn/CsvIndex/CsvIgnore)");
    using var stream = File.OpenRead(csvPath);
    using var reader = new CsvReader(stream, options);

    while (reader.TryReadRecord<AttributedPerson>(out var person))
    {
        Console.WriteLine(
            $"attr: {person.Id} {person.Name} | BirthDate={person.BirthDate:yyyy-MM-dd} | Age={person.Age}");
    }
}

static void RunFluentMapApi(string csvPath, CsvOptions options)
{
    Console.WriteLine("\n[5] Fluent mapper API (CsvMapRegistry)");
    var maps = new CsvMapRegistry();
    maps.Register<FluentEmployee>(map =>
    {
        map.Map(x => x.Id).Name("employee_id");
        map.Map(x => x.Name).Name("display_name");
        map.Map(x => x.StartDate).Name("started_on").Converter(new DateOnlyIsoConverter());
        map.Map(x => x.HourlyRate).Name("hourly_rate");
        map.Map(x => x.InternalNote).Ignore();
    });

    using var stream = File.OpenRead(csvPath);
    using var reader = new CsvReader(stream, options, maps);

    while (reader.TryReadRecord<FluentEmployee>(out var employee))
    {
        Console.WriteLine(
            $"fluent: {employee.Id} {employee.Name} | Start={employee.StartDate:yyyy-MM-dd} | Rate={employee.HourlyRate}");
    }
}

static void RunWriteApi(string outputPath, CsvOptions options)
{
    Console.WriteLine("\n[6] Write API (WriteHeader/WriteRecord/WriteField/NextRecord)");

    var records = new List<AttributedPerson>
    {
        new()
        {
            Id = 10, Name = "Nina Brooks", Email = "nina@example.com", Age = 30, BirthDate = new DateOnly(1995, 4, 18),
            IgnoredAtRuntime = "hidden"
        },
        new()
        {
            Id = 11, Name = "Owen Price", Email = "owen@example.com", Age = 42, BirthDate = new DateOnly(1983, 11, 2),
            IgnoredAtRuntime = "hidden"
        }
    };

    using var stream = File.Create(outputPath);
    using var writer = new CsvWriter(stream, options);

    writer.WriteHeader<AttributedPerson>();
    foreach (var record in records)
    {
        writer.WriteRecord(record);
    }

    writer.WriteField("manual_note");
    writer.WriteField("this row was added with WriteField");
    writer.NextRecord();

    writer.Flush();

    Console.WriteLine($"Wrote file: {outputPath}");
}

static async Task RunAsyncApi(string inputPath, string outputPath, CsvOptions options)
{
    Console.WriteLine("\n[7] Async API (ReadRecordAsync + WriteRecordAsync)");
    var rows = new List<AttributedPerson>();

    await using (var input = File.OpenRead(inputPath))
    await using (var reader = new CsvReader(input, options))
    {
        AttributedPerson? row;
        while ((row = await reader.ReadRecordAsync<AttributedPerson>()) is not null)
        {
            rows.Add(row);
        }
    }

    await using (var output = File.Create(outputPath))
    await using (var writer = new CsvWriter(output, options))
    {
        await writer.WriteHeaderAsync<AttributedPerson>();
        foreach (var row in rows)
        {
            await writer.WriteRecordAsync(row);
        }

        await writer.FlushAsync();
    }

    Console.WriteLine($"Async copied {rows.Count} rows to: {outputPath}");
}

public sealed class AttributedPerson
{
    [CsvIndex(0)] [CsvColumn("person_id")] public int Id { get; set; }

    [CsvColumn("full_name")] public string Name { get; set; } = string.Empty;

    [CsvColumn("email")] public string Email { get; set; } = string.Empty;

    [CsvColumn("age")] public int Age { get; set; }

    [CsvColumn("birth_date")] public DateOnly BirthDate { get; set; }

    [CsvIgnore] public string? IgnoredAtRuntime { get; set; }
}

public sealed class FluentEmployee
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public decimal HourlyRate { get; set; }
    public string? InternalNote { get; set; }
}

public sealed class DateOnlyIsoConverter : ICsvTypeConverter<DateOnly>
{
    private const string IsoPattern = "yyyy-MM-dd";

    public bool TryParse(ReadOnlySpan<char> source, in CsvConverterContext context, out DateOnly value)
    {
        return DateOnly.TryParseExact(source, IsoPattern, context.CultureInfo, DateTimeStyles.None, out value);
    }

    public string Format(DateOnly value, in CsvConverterContext context)
    {
        return value.ToString(IsoPattern, context.CultureInfo);
    }
}