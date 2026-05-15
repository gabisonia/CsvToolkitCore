# CsvToolkit.Core

<p align="center">
  <img src="docs/assets/csvtoolkit-logo-dark.svg" alt="CsvToolkit.Core logo" width="760" />
</p>

[![NuGet version](https://img.shields.io/nuget/vpre/CsvToolkit.Core.svg)](https://www.nuget.org/packages/CsvToolkit.Core)
[![publish](https://github.com/gabisonia/CsvToolkitCore/actions/workflows/publish.yml/badge.svg)](https://github.com/gabisonia/CsvToolkitCore/actions/workflows/publish.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

`CsvToolkit.Core` is a high-performance CSV library targeting `netstandard2.1`, focused on streaming and low allocations with `Span<T>`, `Memory<T>`, and `ArrayPool<T>`.

## Benchmark Note

In the latest focused manual-read benchmark, `CsvToolkit.Core` now beats [`Sep`](https://github.com/nietras/Sep) in `6/6` read scenarios. At `100k` rows, manual reads are `14.190 ms` vs `21.938 ms` for default CSV, `15.873 ms` vs `25.877 ms` for semicolon/high-quote CSV, and `7.179 ms` vs `11.648 ms` for converter-options data. The older full-suite typed POCO table is retained below separately for broader context.

## NuGet

Package name on NuGet.org: `CsvToolkit.Core`

```bash
dotnet add package CsvToolkit.Core
```

## License

This project is licensed under the MIT License. See [`LICENSE`](LICENSE).

## Public API Overview

```csharp
var options = new CsvOptions
{
    DelimiterString = ",", // supports multi-character delimiters too
    DetectDelimiter = false,
    DelimiterCandidates = new[] { ",", ";", "\t", "|" },
    HasHeader = true,
    Quote = '"',
    Escape = '"',
    TrimOptions = CsvTrimOptions.Trim,
    DetectColumnCount = true,
    ReadMode = CsvReadMode.Strict,
    CultureInfo = CultureInfo.InvariantCulture,
    PrepareHeaderForMatch = static (header, _) => header.Trim().ToLowerInvariant(),
    SanitizeForInjection = true
};

using var reader = new CsvReader(streamOrTextReader, options);
using var writer = new CsvWriter(streamOrTextWriter, options);

// Row/field iteration
while (reader.TryReadRow(out _))
{
    int id = reader.GetInt32("id");
    ReadOnlySpan<char> name = reader.GetFieldSpan(1);
    ReadOnlySpan<char> email = reader.GetFieldSpan("email");
    string materialized = reader.GetField("email");
}

// Manual mapping for throughput-sensitive paths
var idIndex = reader.GetFieldIndex("id");
var nameIndex = reader.GetFieldIndex("name");

var state = (IdIndex: idIndex, NameIndex: nameIndex, Items: new List<MyRow>());
reader.ReadRows(state, static (csv, s) =>
{
    s.Items.Add(new MyRow
    {
        Id = csv.GetInt32(s.IdIndex),
        Name = csv.GetField(s.NameIndex)
    });
});

// Dictionary / dynamic
if (reader.TryReadDictionary(out var dict)) { /* header -> value */ }
if (reader.TryReadDynamic(out dynamic dyn)) { /* ExpandoObject */ }

// Strongly typed POCO
while (reader.TryReadRecord<MyRow>(out var record)) { }
writer.WriteHeader<MyRow>();
writer.WriteRecord(new MyRow());
var records = new List<MyRow> { new MyRow() };
writer.WriteRecords(records, writeHeader: false);
writer.WriteField("manual".AsSpan());
writer.WriteField("row".AsSpan());
writer.NextRecord();

// Async
while (await reader.ReadAsync()) { var current = reader.GetRecord<MyRow>(); }
await reader.ReadRecordAsync<MyRow>();
await foreach (var row in reader.GetRecordsAsync<MyRow>()) { }
await writer.WriteRecordAsync(new MyRow());
await writer.WriteRecordsAsync(records, writeHeader: true);

// ADO.NET adapter
using var dataReader = reader.AsDataReader();
```

## Features

- Read from `TextReader` and UTF-8 `Stream`
- Write to `TextWriter` and UTF-8 `Stream`
- Streaming row-by-row parser (no full-file load)
- Quoted fields, escaped quotes, delimiters inside quotes, CRLF/LF handling
- Header support, delimiter/quote/escape/newline configuration
- Multi-character delimiters via `DelimiterString`
- Optional delimiter auto-detection via `DetectDelimiter` + `DelimiterCandidates`
- Trim options and strict/lenient error handling
- Error and validation callbacks: `BadDataFound`, `MissingFieldFound`, `HeaderValidated`, `ReadingExceptionOccurred`
- Header normalization callback: `PrepareHeaderForMatch`
- Field access as `ReadOnlySpan<char>` / `ReadOnlyMemory<char>`
- Name-based low-level field access: `GetFieldSpan(name)`, `GetFieldMemory(name)`, `GetFieldIndex(name, nameIndex)`
- Fast typed manual access: `GetInt32(...)`, `GetDecimal(...)`, `GetDateTime(...)`, `GetBoolean(...)`, and nullable variants
- Projection-style manual reads via `ReadRows(state, static (reader, state) => ...)`
- Typed field access helpers: `GetField<T>(index)` / `GetField<T>(name, nameIndex)`
- Reader APIs: `TryReadRow`, `ReadAsync`, `TryReadDictionary`, `ReadDictionaryAsync`, `TryReadDynamic`
- Record APIs: `TryReadRecord<T>`, `ReadRecordAsync<T>`, `GetRecords<T>`, `GetRecordsAsync<T>`
- `CsvDataReader` adapter via `AsDataReader()`
- POCO mapping with:
  - Attributes: `[CsvColumn]`, `[CsvIndex]`, `[CsvIgnore]`, `[CsvNameIndex]`, `[CsvOptional]`, `[CsvDefault]`, `[CsvConstant]`, `[CsvValidate]`
  - Converter option attributes: `[CsvNullValues]`, `[CsvTrueValues]`, `[CsvFalseValues]`, `[CsvFormats]`, `[CsvNumberStyles]`, `[CsvDateTimeStyles]`, `[CsvCulture]`
  - Fluent mapping: `CsvMapRegistry.Register<T>(...)` with `Optional`, `Default`, `Constant`, `Validate`, `NameIndex`, member converter options
  - Constructor-based record materialization (immutable / constructor-only models)
- Type conversion:
  - primitives, enums, nullable, `DateTime`, `DateOnly`, `TimeOnly`, `Guid`
  - culture-aware parsing/formatting
  - custom converters (`ICsvTypeConverter<T>`)
  - global converter options per type via `CsvOptions.ConverterOptions`
- Writer bulk APIs: `WriteRecords(...)`, `WriteRecordsAsync(...)`
- Manual writer APIs: `WriteField(ReadOnlySpan<char>)`, `WriteField<T>(...)`, `NextRecord()`
- CSV injection sanitization for spreadsheet-safe output (`SanitizeForInjection`)

## Examples

### Read POCOs

```csharp
using var reader = new CsvReader(new StreamReader("people.csv"));
while (reader.TryReadRecord<Person>(out var person))
{
    Console.WriteLine(person.Name);
}
```

### Write POCOs

```csharp
using var writer = new CsvWriter(File.Create("people.csv"), new CsvOptions { NewLine = "\n" });
writer.WriteHeader<Person>();
foreach (var person in people)
{
    writer.WriteRecord(person);
}
```

### Read Without String Allocations

```csharp
var idIndex = reader.GetFieldIndex("id");
var nameIndex = reader.GetFieldIndex("name");

reader.ReadRows((IdIndex: idIndex, NameIndex: nameIndex), static (csv, state) =>
{
    int id = csv.GetInt32(state.IdIndex);
    ReadOnlySpan<char> name = csv.GetFieldSpan(state.NameIndex);
});
```

### Manual Field Writing

```csharp
using var writer = new CsvWriter(File.Create("people.csv"), new CsvOptions { NewLine = "\n" });

writer.WriteField("id".AsSpan());
writer.WriteField("name".AsSpan());
writer.NextRecord();

foreach (var person in people)
{
    Span<char> idBuffer = stackalloc char[16];
    person.Id.TryFormat(idBuffer, out var written, default, CultureInfo.InvariantCulture);
    writer.WriteField(idBuffer[..written]);
    writer.WriteField(person.Name.AsSpan());
    writer.NextRecord();
}
```

### Fluent Mapping

```csharp
var maps = new CsvMapRegistry();
maps.Register<Person>(map =>
{
    map.Map(x => x.Id).Name("person_id");
    map.Map(x => x.Name).Name("full_name");
});
```

### Attribute Mapping and Converter Options

```csharp
public sealed class PersonRow
{
    [CsvColumn("name"), CsvNameIndex(0)]
    public string FirstName { get; set; } = string.Empty;

    [CsvColumn("name"), CsvNameIndex(1)]
    public string LastName { get; set; } = string.Empty;

    [CsvOptional, CsvDefault(18)]
    public int Age { get; set; }

    [CsvConstant("US")]
    public string Country { get; set; } = string.Empty;

    [CsvValidate(nameof(IsValidAge), Message = "Age must be >= 0.")]
    public int CheckedAge { get; set; }

    [CsvTrueValues("Y"), CsvFalseValues("N")]
    public bool Active { get; set; }

    [CsvFormats("dd-MM-yyyy"), CsvCulture("en-GB")]
    public DateTime CreatedAt { get; set; }

    [CsvNullValues("NULL")]
    public decimal? Score { get; set; }

    private static bool IsValidAge(int value) => value >= 0;
}
```

### Converter Option Precedence

For read/write conversion options, resolution order is:

1. Member-level fluent options (`map.Map(...).TrueValues(...).Formats(...)`)
2. Member-level attribute options (`[CsvTrueValues]`, `[CsvFormats]`, etc.)
3. Global type options (`options.ConverterOptions.Configure<T>(...)`)
4. Built-in defaults

Notes:
- Fluent member options override attribute options for the same member.
- Attribute/member options are isolated to that mapped member and do not affect other properties of the same type.
- `ConverterOptions` precedence applies to both `CsvReader` and `CsvWriter`.

## Benchmarks

Benchmarks compare `CsvToolkit.Core` with `CsvHelper` and `Sep` for:

- Typed read/write (default mapping)
- Manual mapped read/write using `ReadRows`, `TryReadRow`, `GetFieldIndex`, `GetFieldSpan`, and `WriteField(ReadOnlySpan<char>)`
- Typed read/write with converter options
- Async typed read/write for stream-backed APIs
- Typed read with duplicate headers (`NameIndex`)
- Dictionary/dynamic read
- Semicolon + high quoting parse
- Dataset sizes: `10k` and `100k` rows

### Technical Docs

If you want deeper implementation details and rationale, start here:

- [Technical Architecture](docs/technical-architecture.md): components, data flow, and technologies used.
- [Performance Design Decisions](docs/performance-design-decisions.md): why hot paths are fast and the tradeoffs behind each optimization.

Run all benchmarks non-interactively:

```bash
dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks -- --filter "*CsvReadWriteBenchmarks*"
```

Run one benchmark (faster while iterating):

```bash
dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks -- --filter "*CsvReadWriteBenchmarks.CsvToolkitCore_ReadTyped_Stream*"
```

Run manual mapping vs Sep-focused benchmarks:

```bash
dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks -- --anyCategories SepCompare
```

Run only manual read benchmarks in that comparison set:

```bash
dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks -- --allCategories ManualRead SepCompare
```

Run the async stream-focused benchmarks:

```bash
dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks -- --filter "*CsvReadWriteBenchmarks.CsvToolkitCore_*Async*"
```

Run from IDE:

- Project: `benchmarks/CsvToolkit.Benchmarks`
- Configuration: `Release`
- Program arguments: `--filter "*CsvReadWriteBenchmarks*"`

If you run without `--filter`, BenchmarkDotNet enters interactive selection mode and waits for input.

Benchmark dataset generation is deterministic (`Random` seed-based) inside benchmark setup.

### Latest Results

Run date: `2026-03-07`  
Machine: `Apple M3 Pro` (`11` logical / `11` physical cores)  
OS: `macOS Tahoe 26.3 (25D125) [Darwin 25.3.0]`  
Runtime: `.NET 10.0.0`  
Command: `dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks -- --filter "*CsvReadWriteBenchmarks*"`

Note: the table below is the saved full-suite typed POCO run. A newer focused manual mapping vs `Sep`
run is included after the common scenario table.

Common scenarios benchmarked across all three libraries:

| Scenario | RowCount | CsvToolkit.Core (Mean / Alloc) | CsvHelper (Mean / Alloc) | Sep (Mean / Alloc) | Time Winner | Allocation Winner |
|--------- |---------:|-------------------------------:|--------------------------:|-------------------:|------------:|------------------:|
| ReadTyped | 10,000 | 3.907 ms / 0.99 MB | 4.879 ms / 3.76 MB | 2.097 ms / 0.92 MB | Sep (`1.86x` vs CsvToolkit) | Sep (`1.08x` lower vs CsvToolkit) |
| WriteTyped | 10,000 | 2.038 ms / 1.05 MB | 2.936 ms / 3.45 MB | 1.145 ms / 2.01 MB | Sep (`1.78x`) | CsvToolkit (`1.92x` lower vs Sep) |
| ReadTyped_SemicolonHighQuote | 10,000 | 4.661 ms / 0.99 MB | 5.292 ms / 3.76 MB | 2.310 ms / 0.92 MB | Sep (`2.02x`) | Sep (`1.08x` lower) |
| ReadTyped_WithConverterOptions | 10,000 | 2.606 ms / 1.33 MB | 3.193 ms / 2.67 MB | 1.088 ms / 0.39 MB | Sep (`2.40x`) | Sep (`3.43x` lower) |
| WriteTyped_WithConverterOptions | 10,000 | 2.197 ms / 0.54 MB | 2.434 ms / 2.46 MB | 0.746 ms / 0.70 MB | Sep (`2.94x`) | CsvToolkit (`1.29x` lower vs Sep) |
| ReadTyped | 100,000 | 30.120 ms / 9.30 MB | 46.449 ms / 36.72 MB | 21.639 ms / 9.23 MB | Sep (`1.39x`) | Sep (`1.01x` lower) |
| WriteTyped | 100,000 | 17.117 ms / 16.05 MB | 22.847 ms / 39.40 MB | 12.028 ms / 16.01 MB | Sep (`1.42x`) | Sep (`1.00x` lower) |
| ReadTyped_SemicolonHighQuote | 100,000 | 32.651 ms / 9.30 MB | 49.259 ms / 36.72 MB | 23.793 ms / 9.23 MB | Sep (`1.37x`) | Sep (`1.01x` lower) |
| ReadTyped_WithConverterOptions | 100,000 | 23.674 ms / 12.80 MB | 27.904 ms / 25.81 MB | 10.971 ms / 3.82 MB | Sep (`2.16x`) | Sep (`3.35x` lower) |
| WriteTyped_WithConverterOptions | 100,000 | 14.878 ms / 8.04 MB | 19.270 ms / 26.60 MB | 7.409 ms / 9.93 MB | Sep (`2.01x`) | CsvToolkit (`1.24x` lower vs Sep) |

Focused manual mapping vs `Sep` run:

Run date: `2026-05-15`
Runtime: `.NET 10.0.5`
Command: `dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks/CsvToolkit.Benchmarks.csproj -- --anyCategories SepCompare`

Read rows were refreshed after adding the no-copy buffered row fast path, `ReadRows`, typed manual getters, and span-based custom parsers with:
`dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks/CsvToolkit.Benchmarks.csproj -- --allCategories ManualRead SepCompare`.

| Scenario | RowCount | CsvToolkit Manual (Mean / Alloc) | Sep (Mean / Alloc) | Time Winner | Allocation Winner |
|--------- |---------:|----------------------------------:|-------------------:|------------:|------------------:|
| ReadTyped_ManualMapping | 10,000 | 1.402 ms / 398.01 KB | 2.213 ms / 941.98 KB | CsvToolkit (`1.58x`) | CsvToolkit (`2.37x` lower) |
| ReadTyped_ManualMapping_SemicolonHighQuote | 10,000 | 1.616 ms / 398.03 KB | 2.403 ms / 941.98 KB | CsvToolkit (`1.49x`) | CsvToolkit (`2.37x` lower) |
| ReadTyped_ManualMapping_WithConverterOptions | 10,000 | 0.677 ms / 7.30 KB | 1.142 ms / 395.09 KB | CsvToolkit (`1.69x`) | CsvToolkit (`54.12x` lower) |
| WriteTyped_ManualMapping | 10,000 | 1.700 ms / 2,038.09 KB | 1.312 ms / 2,054.48 KB | Sep (`1.30x`) | CsvToolkit (`1.01x` lower) |
| WriteTyped_ManualMapping_WithConverterOptions | 10,000 | 1.142 ms / 501.75 KB | 0.780 ms / 715.11 KB | Sep (`1.46x`) | CsvToolkit (`1.43x` lower) |
| ReadTyped_ManualMapping | 100,000 | 14.190 ms / 3,984.52 KB | 21.938 ms / 9,450.38 KB | CsvToolkit (`1.55x`) | CsvToolkit (`2.37x` lower) |
| ReadTyped_ManualMapping_SemicolonHighQuote | 100,000 | 15.873 ms / 3,984.55 KB | 25.877 ms / 9,450.38 KB | CsvToolkit (`1.63x`) | CsvToolkit (`2.37x` lower) |
| ReadTyped_ManualMapping_WithConverterOptions | 100,000 | 7.179 ms / 7.30 KB | 11.648 ms / 3,910.72 KB | CsvToolkit (`1.62x`) | CsvToolkit (`535.71x` lower) |
| WriteTyped_ManualMapping | 100,000 | 17.272 ms / 16,373.89 KB | 13.091 ms / 16,390.40 KB | Sep (`1.32x`) | CsvToolkit (`1.00x` lower) |
| WriteTyped_ManualMapping_WithConverterOptions | 100,000 | 11.677 ms / 8,181.85 KB | 8.335 ms / 10,167.00 KB | Sep (`1.40x`) | CsvToolkit (`1.24x` lower) |

Focused manual mapping takeaway:
- CsvToolkit now wins all `6/6` focused manual read scenarios against `Sep`.
- CsvToolkit read allocations are lower in all focused manual read scenarios because simple unquoted rows can reuse input-buffer slices.
- `Sep` remains faster in the focused manual write rows, while CsvToolkit has lower write allocations.

Additional scenarios:

| Scenario | RowCount | CsvToolkit.Core (Mean / Alloc) | CsvHelper (Mean / Alloc) | Time Winner | Allocation Winner |
|--------- |---------:|-------------------------------:|--------------------------:|------------:|------------------:|
| ReadTyped_DuplicateHeader_NameIndex | 10,000 | 0.927 ms / 1.17 MB | 1.605 ms / 1.78 MB | CsvToolkit (`1.73x`) | CsvToolkit (`1.51x` lower) |
| ReadTyped_DuplicateHeader_NameIndex | 100,000 | 8.585 ms / 12.23 MB | 14.277 ms / 17.64 MB | CsvToolkit (`1.66x`) | CsvToolkit (`1.44x` lower) |
| ReadDictionary vs ReadDynamic | 10,000 | 1.762 ms / 5.26 MB | 4.312 ms / 8.00 MB | CsvToolkit (`2.45x`) | CsvToolkit (`1.52x` lower) |
| ReadDictionary vs ReadDynamic | 100,000 | 18.355 ms / 52.64 MB | 43.219 ms / 79.41 MB | CsvToolkit (`2.35x`) | CsvToolkit (`1.51x` lower) |

Observed trend:
- In the older full common-scenario run shown above, `Sep` is the fastest option in `10/10`.
- In that full run, `Sep` wins allocations in `7/10` common scenarios and `CsvToolkit.Core` wins `3/10`, all on typed write scenarios.
- In that full run, `CsvToolkit.Core` beats `CsvHelper` in `10/10` common scenarios.
- In the refreshed focused manual read comparison, `CsvToolkit.Core` beats `Sep` in `6/6` read scenarios.
- In the extra `DuplicateHeader_NameIndex` scenario, `CsvToolkit.Core` beats `CsvHelper` at both sizes.
- In the extra `ReadDictionary vs ReadDynamic` scenario, `CsvToolkit.Core` beats `CsvHelper` at both sizes.
- Important caveat for the full-suite table: `Sep` is benchmarked there through explicit/manual column mapping and writing, while the listed `CsvToolkit.Core` rows use higher-level typed POCO APIs. Use the focused manual mapping table for the direct low-level comparison.

Benchmark run time:
- Benchmark execution: `00:14:31` (`871.21 sec`)
- Global total: `00:14:37` (`877.11 sec`)

Benchmark artifacts:
- Generated local output: `BenchmarkDotNet.Artifacts/results/`
