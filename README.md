# CsvToolkit.Core

<p align="center">
  <img src="docs/assets/csvtoolkit-logo-dark.svg" alt="CsvToolkit.Core logo" width="760" />
</p>

[![NuGet version](https://img.shields.io/nuget/vpre/CsvToolkit.Core.svg)](https://www.nuget.org/packages/CsvToolkit.Core)
[![publish](https://github.com/gabisonia/CsvToolkitCore/actions/workflows/publish.yml/badge.svg)](https://github.com/gabisonia/CsvToolkitCore/actions/workflows/publish.yml)
[![publish-beta](https://github.com/gabisonia/CsvToolkitCore/actions/workflows/publish-beta.yml/badge.svg)](https://github.com/gabisonia/CsvToolkitCore/actions/workflows/publish-beta.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

`CsvToolkit.Core` is a high-performance CSV library for `net10.0` focused on streaming and low allocations with `Span<T>`, `Memory<T>`, and `ArrayPool<T>`.

## NuGet

Package name on NuGet.org: `CsvToolkit.Core`

```bash
dotnet add package CsvToolkit.Core --prerelease
```

## License

This project is licensed under the MIT License. See [`LICENSE`](LICENSE).

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

Run date: `2026-03-03`  
Machine: `Apple M3 Pro`  
Runtime: `.NET 10.0.0`  
Command: `dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks -- --filter "*CsvReadWriteBenchmarks*"`

| Method                                      | RowCount | Mean     | Error    | StdDev   | Ratio | Gen0      | Gen1     | Gen2     | Allocated | Alloc Ratio |
|-------------------------------------------- |--------- |---------:|---------:|---------:|------:|----------:|---------:|---------:|----------:|------------:|
| CsvToolkitCore_WriteTyped_Stream            | 100000   | 22.52 ms | 0.403 ms | 0.377 ms |  0.46 | 3250.0000 | 406.2500 | 375.0000 |  38.86 MB |        2.02 |
| CsvHelper_WriteTyped_Stream                 | 100000   | 23.99 ms | 0.169 ms | 0.150 ms |  0.48 | 3281.2500 | 656.2500 | 343.7500 |  39.41 MB |        2.05 |
| CsvToolkitCore_ReadDictionary_Stream        | 100000   | 25.91 ms | 0.098 ms | 0.076 ms |  0.52 | 6593.7500 |        - |        - |  52.64 MB |        2.74 |
| CsvHelper_ReadDynamic_Stream                | 100000   | 42.88 ms | 0.164 ms | 0.153 ms |  0.87 | 9916.6667 | 250.0000 |        - |  79.41 MB |        4.14 |
| CsvHelper_ReadTyped_Stream                  | 100000   | 45.54 ms | 0.367 ms | 0.325 ms |  0.92 | 4545.4545 | 181.8182 |        - |  36.72 MB |        1.91 |
| CsvToolkitCore_ReadTyped_SemicolonHighQuote | 100000   | 47.70 ms | 0.679 ms | 0.635 ms |  0.96 | 2363.6364 |        - |        - |   19.2 MB |        1.00 |
| CsvHelper_ReadTyped_SemicolonHighQuote      | 100000   | 49.05 ms | 0.135 ms | 0.120 ms |  0.99 | 4600.0000 | 200.0000 |        - |  36.72 MB |        1.91 |
| CsvToolkitCore_ReadTyped_Stream             | 100000   | 49.49 ms | 0.422 ms | 0.395 ms |  1.00 | 2363.6364 |        - |        - |   19.2 MB |        1.00 |

Outlier hints:
- `CsvHelper_WriteTyped_Stream`: 1 outlier removed (`25.78 ms`)
- `CsvToolkitCore_ReadDictionary_Stream`: 3 outliers removed (`26.21 ms..28.93 ms`)
- `CsvHelper_ReadTyped_Stream`: 1 outlier removed (`46.74 ms`)
- `CsvHelper_ReadTyped_SemicolonHighQuote`: 1 outlier removed (`50.79 ms`)

Benchmark run time:
- Benchmark execution: `00:02:29` (`149.52 sec`)
- Global total: `00:02:33` (`153.73 sec`)

Raw benchmark artifacts:
- `BenchmarkDotNet.Artifacts/results/CsvToolkit.Core.Benchmarks.CsvReadWriteBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/CsvToolkit.Core.Benchmarks.CsvReadWriteBenchmarks-report.csv`
