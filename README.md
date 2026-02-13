# CsvToolkit.Core

<p align="center">
  <img src="docs/assets/csvtoolkit-logo-dark.svg" alt="CsvToolkit.Core logo" width="760" />
</p>

[![NuGet version](https://img.shields.io/nuget/vpre/CsvToolkit.Core.svg)](https://www.nuget.org/packages/CsvToolkit.Core)
[![publish](https://github.com/gabisonia/CsvToolkitCore/actions/workflows/publish.yml/badge.svg)](https://github.com/gabisonia/CsvToolkitCore/actions/workflows/publish.yml)
[![publish-beta](https://github.com/gabisonia/CsvToolkitCore/actions/workflows/publish-beta.yml/badge.svg)](https://github.com/gabisonia/CsvToolkitCore/actions/workflows/publish-beta.yml)

`CsvToolkit.Core` is a high-performance CSV library for `net10.0` focused on streaming and low allocations with `Span<T>`, `Memory<T>`, and `ArrayPool<T>`.

## NuGet

Package name on NuGet.org: `CsvToolkit.Core`

```bash
dotnet add package CsvToolkit.Core --prerelease
```

## Public API Overview

```csharp
var options = new CsvOptions
{
    Delimiter = ',',
    HasHeader = true,
    Quote = '"',
    Escape = '"',
    TrimOptions = CsvTrimOptions.Trim,
    DetectColumnCount = true,
    ReadMode = CsvReadMode.Strict,
    CultureInfo = CultureInfo.InvariantCulture
};

using var reader = new CsvReader(streamOrTextReader, options);
using var writer = new CsvWriter(streamOrTextWriter, options);

// Row/field iteration
while (reader.TryReadRow(out var row))
{
    ReadOnlySpan<char> field = row.GetFieldSpan(0);
    string materialized = row.GetFieldString(1);
}

// Dictionary / dynamic
if (reader.TryReadDictionary(out var dict)) { /* header -> value */ }
if (reader.TryReadDynamic(out dynamic dyn)) { /* ExpandoObject */ }

// Strongly typed POCO
while (reader.TryReadRecord<MyRow>(out var record)) { }
writer.WriteHeader<MyRow>();
writer.WriteRecord(new MyRow());

// Async
await reader.ReadAsync();
await writer.WriteRecordAsync(new MyRow());
```

## Features

- Read from `TextReader` and UTF-8 `Stream`
- Write to `TextWriter` and UTF-8 `Stream`
- Streaming row-by-row parser (no full-file load)
- Quoted fields, escaped quotes, delimiters inside quotes, CRLF/LF handling
- Header support, delimiter/quote/escape/newline configuration
- Trim options and strict/lenient error handling with callback context
- Field access as `ReadOnlySpan<char>` / `ReadOnlyMemory<char>`
- POCO mapping with:
  - Attributes: `[CsvColumn]`, `[CsvIndex]`, `[CsvIgnore]`
  - Fluent mapping: `CsvMapRegistry.Register<T>(...)`
- Type conversion:
  - primitives, enums, nullable, `DateTime`, `DateOnly`, `TimeOnly`, `Guid`
  - culture-aware parsing/formatting
  - custom converters (`ICsvTypeConverter<T>`)
- Async read/write entry points

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

## Benchmarks

Benchmarks compare `CsvToolkit.Core` with `CsvHelper` for:

- Typed read (`100k` rows)
- Dictionary/dynamic read
- Typed write
- Semicolon + high quoting parse

### Technical Docs

If you want deeper implementation details and rationale, start here:

- [Technical Architecture](docs/technical-architecture.md): components, data flow, and technologies used.
- [Performance Design Decisions](docs/performance-design-decisions.md): why hot paths are fast and the tradeoffs behind each optimization.

Run all benchmarks non-interactively:

```bash
dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks -- --filter "*"
```

Run one benchmark (faster while iterating):

```bash
dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks -- --filter "*CsvReadWriteBenchmarks.CsvToolkitCore_ReadTyped_Stream*"
```

Run from IDE:

- Project: `benchmarks/CsvToolkit.Benchmarks`
- Configuration: `Release`
- Program arguments: `--filter "*"`

If you run without `--filter`, BenchmarkDotNet enters interactive selection mode and waits for input.

Benchmark dataset generation is deterministic (`Random` seed-based) inside benchmark setup.

### Latest Results

Run date: `2026-02-10`  
Machine: `Apple M3 Pro`  
Runtime: `.NET 10.0.0`  
Command: `dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks -- --filter "*CsvReadWriteBenchmarks*"`

| Method                                  | RowCount | Mean     | Error    | StdDev   | Ratio | Gen0      | Gen1     | Gen2     | Allocated | Alloc Ratio |
|---------------------------------------- |--------- |---------:|---------:|---------:|------:|----------:|---------:|---------:|----------:|------------:|
| CsvToolkitCore_WriteTyped_Stream        | 100000   | 20.76 ms | 0.110 ms | 0.098 ms |  0.42 | 1593.7500 | 406.2500 | 343.7500 |  25.97 MB |        1.35 |
| CsvHelper_WriteTyped_Stream             | 100000   | 24.16 ms | 0.135 ms | 0.120 ms |  0.48 | 3281.2500 | 656.2500 | 343.7500 |   39.4 MB |        2.05 |
| CsvToolkitCore_ReadDictionary_Stream    | 100000   | 26.78 ms | 0.060 ms | 0.050 ms |  0.54 | 6593.7500 |        - |        - |  52.64 MB |        2.74 |
| CsvHelper_ReadDynamic_Stream            | 100000   | 43.98 ms | 0.238 ms | 0.186 ms |  0.88 | 9916.6667 | 250.0000 |        - |  79.41 MB |        4.14 |
| CsvHelper_ReadTyped_Stream              | 100000   | 45.86 ms | 0.318 ms | 0.298 ms |  0.92 | 4545.4545 | 181.8182 |        - |  36.72 MB |        1.91 |
| CsvToolkitCore_ReadTyped_SemicolonHighQuote | 100000   | 48.81 ms | 0.121 ms | 0.107 ms |  0.98 | 2400.0000 |        - |        - |   19.2 MB |        1.00 |
| CsvHelper_ReadTyped_SemicolonHighQuote  | 100000   | 49.88 ms | 0.187 ms | 0.166 ms |  1.00 | 4545.4545 | 181.8182 |        - |  36.72 MB |        1.91 |
| CsvToolkitCore_ReadTyped_Stream         | 100000   | 49.90 ms | 0.206 ms | 0.183 ms |  1.00 | 2400.0000 |        - |        - |   19.2 MB |        1.00 |

Raw benchmark artifacts:
- `BenchmarkDotNet.Artifacts/results/CsvToolkit.Benchmarks.CsvReadWriteBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/CsvToolkit.Benchmarks.CsvReadWriteBenchmarks-report.csv`
