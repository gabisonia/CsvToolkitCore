# CsvToolkit.Core

<p align="center">
  <img src="docs/assets/csvtoolkit-logo-dark.svg" alt="CsvToolkit.Core logo" width="760" />
</p>

[![NuGet version](https://img.shields.io/nuget/vpre/CsvToolkit.Core.svg)](https://www.nuget.org/packages/CsvToolkit.Core)
[![publish](https://github.com/gabisonia/CsvToolkitCore/actions/workflows/publish.yml/badge.svg)](https://github.com/gabisonia/CsvToolkitCore/actions/workflows/publish.yml)
[![publish-beta](https://github.com/gabisonia/CsvToolkitCore/actions/workflows/publish-beta.yml/badge.svg)](https://github.com/gabisonia/CsvToolkitCore/actions/workflows/publish-beta.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

`CsvToolkit.Core` is a high-performance CSV library targeting `netstandard2.1`, focused on streaming and low allocations with `Span<T>`, `Memory<T>`, and `ArrayPool<T>`.

## Benchmark Note

[`Sep`](https://github.com/nietras/Sep) is still the fastest option in the latest full benchmark run, but `CsvToolkit.Core` now beats `CsvHelper` in `10/10` common typed scenarios and wins allocations in `3/10` of those scenarios, all on typed write paths. In the current `100k`-row full run, `CsvToolkit.Core` writes typed POCOs in `17.1 ms` vs `22.8 ms` for `CsvHelper`, and writes typed records with converter options in `14.9 ms` vs `19.3 ms`, while allocating far less in both cases. If your only KPI is absolute CSV throughput, [`Sep`](https://github.com/nietras/Sep) remains the speed ceiling; `CsvToolkit.Core` is now much closer while keeping a higher-level typed API and broader feature coverage.

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
    int id = reader.GetField<int>("id");
    ReadOnlySpan<char> name = reader.GetFieldSpan(1);
    string materialized = reader.GetField("email");
}

// Dictionary / dynamic
if (reader.TryReadDictionary(out var dict)) { /* header -> value */ }
if (reader.TryReadDynamic(out dynamic dyn)) { /* ExpandoObject */ }

// Strongly typed POCO
while (reader.TryReadRecord<MyRow>(out var record)) { }
writer.WriteHeader<MyRow>();
writer.WriteRecord(new MyRow());
var records = new List<MyRow> { new MyRow() };
writer.WriteRecords(records, writeHeader: false);

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
while (reader.TryReadRow(out var row))
{
    ReadOnlySpan<char> id = row.GetFieldSpan(0);
    // parse directly from span
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
- Typed read/write with converter options
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

Additional scenarios:

| Scenario | RowCount | CsvToolkit.Core (Mean / Alloc) | CsvHelper (Mean / Alloc) | Time Winner | Allocation Winner |
|--------- |---------:|-------------------------------:|--------------------------:|------------:|------------------:|
| ReadTyped_DuplicateHeader_NameIndex | 10,000 | 0.927 ms / 1.17 MB | 1.605 ms / 1.78 MB | CsvToolkit (`1.73x`) | CsvToolkit (`1.51x` lower) |
| ReadTyped_DuplicateHeader_NameIndex | 100,000 | 8.585 ms / 12.23 MB | 14.277 ms / 17.64 MB | CsvToolkit (`1.66x`) | CsvToolkit (`1.44x` lower) |
| ReadDictionary vs ReadDynamic | 10,000 | 1.762 ms / 5.26 MB | 4.312 ms / 8.00 MB | CsvToolkit (`2.45x`) | CsvToolkit (`1.52x` lower) |
| ReadDictionary vs ReadDynamic | 100,000 | 18.355 ms / 52.64 MB | 43.219 ms / 79.41 MB | CsvToolkit (`2.35x`) | CsvToolkit (`1.51x` lower) |

Observed trend from this run:
- Across the `10` common scenarios benchmarked for all three libraries, `Sep` is the fastest option in `10/10`.
- On allocations, `Sep` wins `7/10` of those common scenarios and `CsvToolkit.Core` wins `3/10`, all on typed write scenarios.
- `CsvToolkit.Core` beats `CsvHelper` in `10/10` of those common scenarios.
- In the extra `DuplicateHeader_NameIndex` scenario, `CsvToolkit.Core` beats `CsvHelper` at both sizes.
- In the extra `ReadDictionary vs ReadDynamic` scenario, `CsvToolkit.Core` beats `CsvHelper` at both sizes.
- Important caveat: `Sep` is benchmarked here through explicit/manual column mapping and writing, while `CsvToolkit.Core` and `CsvHelper` use higher-level typed POCO APIs. Treat `Sep` as a low-level throughput ceiling, not a direct ergonomic equivalent.

Benchmark run time:
- Benchmark execution: `00:14:31` (`871.21 sec`)
- Global total: `00:14:37` (`877.11 sec`)

Benchmark artifacts:
- Generated local output: `BenchmarkDotNet.Artifacts/results/`
