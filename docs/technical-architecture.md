# Technical Architecture

This document explains what technology `CsvToolkit.Core` uses, how data flows through the system, and why major design choices were made.

## Technology Stack

- Runtime: `.NET 10` (`net10.0`)
- Language: `C#`
- Core performance primitives:
  - `ReadOnlySpan<char>` / `ReadOnlyMemory<char>`
  - `ArrayPool<T>`
  - `ISpanFormattable`
  - UTF-8 `Decoder` / `Encoder`
- Mapping internals:
  - Reflection + expression-compiled accessors
- Validation and benchmarking:
  - `xUnit` in `tests/CsvToolkit.Tests`
  - `BenchmarkDotNet` in `benchmarks/CsvToolkit.Benchmarks`
  - `CsvHelper` used as a benchmark comparison baseline

## Component Overview

| Component | Purpose | Key Files |
| --- | --- | --- |
| `CsvReader` | Public read API: row, dictionary, dynamic, POCO modes | `src/CsvToolkit.Core/CsvReader.cs` |
| `CsvParser` | Low-level CSV state machine | `src/CsvToolkit.Core/Internal/CsvParser.cs` |
| `CsvRowBuffer` + pooled buffers | Store row text and field tokens with low allocations | `src/CsvToolkit.Core/Internal/CsvRowBuffer.cs`, `src/CsvToolkit.Core/Internal/PooledCharBuffer.cs`, `src/CsvToolkit.Core/Internal/PooledList.cs` |
| `CsvRow` | Field access (`Span`, `Memory`, `string`) | `src/CsvToolkit.Core/CsvRow.cs` |
| `CsvWriter` | Public write API for fields and records | `src/CsvToolkit.Core/CsvWriter.cs` |
| Stream/text adapters | Unified char input/output over `TextReader` and UTF-8 streams | `src/CsvToolkit.Core/Internal/TextReaderInput.cs`, `src/CsvToolkit.Core/Internal/Utf8StreamInput.cs`, `src/CsvToolkit.Core/Internal/TextWriterOutput.cs`, `src/CsvToolkit.Core/Internal/Utf8StreamOutput.cs` |
| Mapping registry | Attribute/fluent map resolution and caching | `src/CsvToolkit.Core/Mapping/CsvMapRegistry.cs`, `src/CsvToolkit.Core/Mapping/CsvMapBuilder.cs` |
| Type conversion | Built-in and custom converter dispatch | `src/CsvToolkit.Core/TypeConversion/CsvValueConverter.cs`, `src/CsvToolkit.Core/TypeConversion/CsvConverterRegistry.cs` |

## Read Path (Data Flow)

1. Caller creates `CsvReader` with either `TextReader` or `Stream`.
2. Reader normalizes options (`Clone` + `Validate`) and sets up parser input adapter.
3. `CsvParser` scans input characters and handles CSV state (`inQuotes`, delimiter/newline handling, escapes).
4. `CsvRowBuffer` records field boundaries and row text in pooled memory.
5. Reader exposes one of several materialization modes:
   - `CsvRow` for low-allocation field slicing
   - `Dictionary<string, string?>` / dynamic for flexible schemas
   - POCO mapping via map registry + converters

Key behavior:

- Header initialization is done once, then cached.
- Column index resolution for POCO members is cached per record type.
- Generated fallback column names (`Column0`, `Column1`, ...) are cached.

## Write Path (Data Flow)

1. Caller creates `CsvWriter` with `TextWriter` or `Stream`.
2. Writer emits delimiters/newlines according to `CsvOptions`.
3. Field writing determines quoting only when necessary:
   - delimiter present
   - quote present
   - newline present
   - leading/trailing whitespace
4. Quote characters inside fields are escaped while streaming segments to output.
5. Typed values are formatted through:
   - `ISpanFormattable` fast path when available
   - converter registry / fallback formatting otherwise

## Why This Architecture Performs Well

- Streaming design keeps peak memory roughly proportional to row size, not file size.
- Span/memory APIs avoid forced string allocations in hot loops.
- `ArrayPool<T>` reuse reduces GC pressure from temporary buffers.
- Reader/writer support UTF-8 streams directly to avoid extra glue layers in user code.
- Mapping and conversion caches turn repeated reflection/type-inspection work into one-time setup.

## Important Technical Decisions

### Decision: Keep parser as a dedicated state machine

Why:

- CSV correctness requires nuanced handling of quotes, escapes, and embedded newlines.
- A state machine gives predictable behavior and linear complexity.

Tradeoff:

- Implementation complexity is higher than split-based parsers.

### Decision: Separate sync and async implementations on hot path

Why:

- Sync users avoid async overhead.
- Async users get proper cancellation and non-blocking I/O.

Tradeoff:

- There is duplicated control-flow logic to maintain.

### Decision: Expose both ergonomic and low-level APIs

Why:

- `TryReadDictionary` / `TryReadRecord<T>` are productive for business code.
- `TryReadRow` + `GetFieldSpan` is better for high-throughput/low-allocation pipelines.

Tradeoff:

- API surface area is larger and requires clearer documentation.

### Decision: Strict vs lenient error handling modes

Why:

- Some workloads require fail-fast validation (`Strict`).
- Others require ingest-with-observability (`Lenient` + callback).

Tradeoff:

- More branches in error paths and more configuration combinations to test.

## Correctness and Safety Backing

Coverage examples in tests:

- Quoted delimiter handling
- Embedded newlines in quoted fields
- Escaped quote parsing/writing
- Custom delimiter/quote/escape combinations
- Header/no-header mapping paths
- Culture-aware conversion
- Strict and lenient error behavior
- Async read/write paths with cancellation

See:

- `tests/CsvToolkit.Tests/Parsing/CsvReaderParsingTests.cs`
- `tests/CsvToolkit.Tests/Mapping/CsvMappingTests.cs`
- `tests/CsvToolkit.Tests/CsvWriterTests.cs`

## Benchmarking and Performance Validation

Benchmark entry point:

- `benchmarks/CsvToolkit.Benchmarks/Program.cs`
- `benchmarks/CsvToolkit.Benchmarks/CsvReadWriteBenchmarks.cs`

Run all:

```bash
dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks -- --filter "*"
```

Run focused benchmark:

```bash
dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks -- --filter "*CsvReadWriteBenchmarks.CsvToolkitCore_ReadTyped_Stream*"
```

## Practical Guidance

- Use `CsvReader.TryReadRow` + `GetFieldSpan` for lowest allocation.
- Use POCO mapping when maintainability is more important than absolute minimum allocations.
- Prefer stream constructors for very large files and UTF-8 workflows.
- Use `CsvOptions.CultureInfo` explicitly for locale-sensitive numeric/date data.
