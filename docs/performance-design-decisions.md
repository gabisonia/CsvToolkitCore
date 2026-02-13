# Performance Design Decisions

This document explains why `CsvToolkit.Core` performs well, what technologies are used in hot paths, and what tradeoffs were accepted to get that performance profile.

## Scope

- Parsing and writing throughput
- Allocation behavior in read/write paths
- Mapping and conversion overhead
- Practical tuning knobs exposed through `CsvOptions`

## Performance Goals

- Keep parsing and writing strictly streaming (no full-file buffering).
- Keep hot-path allocations near zero when callers stay on span/memory APIs.
- Support both sync and async APIs without penalizing synchronous scenarios.
- Preserve CSV correctness for quoting, escaping, delimiter variance, and line-ending variance.

## Measurement Setup

Benchmarks live in `benchmarks/CsvToolkit.Benchmarks/CsvReadWriteBenchmarks.cs` and are run with BenchmarkDotNet.

- Dataset size: `100_000` rows
- Workloads:
  - Typed read from UTF-8 stream
  - Dictionary/dynamic read
  - Typed write to stream
  - Semicolon + high quoting read stress case
- Baseline library: `CsvHelper`

Run:

```bash
dotnet run -c Release --project benchmarks/CsvToolkit.Benchmarks -- --filter "*CsvReadWriteBenchmarks*"
```

## Core Decisions and Rationale

### 1. Single-pass streaming parser

Code:

- `src/CsvToolkit.Core/Internal/CsvParser.cs`
- `src/CsvToolkit.Core/Internal/CsvRowBuffer.cs`

Why it helps:

- Parser walks characters once and emits field boundaries incrementally.
- No full-document intermediate structure is created before user consumption.
- Enables true row-by-row processing over large inputs.

Tradeoff:

- Parser state machine is more complex than split-based implementations.

### 2. Dedicated sync and async loops

Code:

- `src/CsvToolkit.Core/Internal/CsvParser.cs` (`TryReadRowCore`, `TryReadRowCoreAsync`)

Why it helps:

- Sync path avoids async state-machine overhead.
- Async path keeps cancellation and non-blocking I/O explicit.
- Avoids per-character branching for "sync or async?" inside a shared loop.

Tradeoff:

- Logic exists in two places, increasing maintenance cost.

### 3. Tokenized row representation over pooled buffers

Code:

- `src/CsvToolkit.Core/Internal/PooledCharBuffer.cs`
- `src/CsvToolkit.Core/Internal/PooledList.cs`
- `src/CsvToolkit.Core/Internal/CsvRowBuffer.cs`
- `src/CsvToolkit.Core/CsvRow.cs`

Why it helps:

- Row text is stored once in a pooled char buffer.
- Fields are represented by `(start, length, wasQuoted)` metadata, not copied strings.
- `GetFieldSpan` / `GetFieldMemory` can return direct slices without allocating strings.

Tradeoff:

- Memory lifetime is tied to row iteration semantics; callers needing persistence must materialize strings or copy data.

### 4. UTF-8 stream adapters with pooled byte buffers

Code:

- `src/CsvToolkit.Core/Internal/Utf8StreamInput.cs`
- `src/CsvToolkit.Core/Internal/Utf8StreamOutput.cs`
- `src/CsvToolkit.Core/Internal/TextReaderInput.cs`
- `src/CsvToolkit.Core/Internal/TextWriterOutput.cs`

Why it helps:

- Stream APIs avoid forcing `TextReader`/`TextWriter` construction in caller code.
- Decoder/encoder + pooled byte arrays reduce transient allocations and keep throughput stable.
- Supports both char-based and UTF-8 byte-based sources/sinks with one parser/writer core.

Tradeoff:

- Additional adapter layer adds implementation surface area.

### 5. Fast-path numeric and primitive conversion with cached type metadata

Code:

- `src/CsvToolkit.Core/TypeConversion/CsvValueConverter.cs`

Why it helps:

- `ConcurrentDictionary<Type, BuiltInTypeInfo>` caches type classification work.
- Built-in converters parse from spans directly for primitives, enums, and common date/time types.
- Bool `'1'/'0'` path avoids extra string creation.

Tradeoff:

- Small persistent cache memory footprint for encountered types.

### 6. Mapping metadata is compiled and reused

Code:

- `src/CsvToolkit.Core/Mapping/CsvMapRegistry.cs`
- `src/CsvToolkit.Core/CsvReader.cs` (`ResolveFieldIndices`)

Why it helps:

- Property getters/setters are expression-compiled once per type map.
- Field index resolution is cached in `_memberIndexCache`.
- Avoids repeated reflection and repeated header lookup work per row.

Tradeoff:

- Initial first-use cost per mapped type.

### 7. Writer fast paths avoid unnecessary string formatting

Code:

- `src/CsvToolkit.Core/CsvWriter.cs`

Why it helps:

- `ISpanFormattable` values are formatted into stack/pooled char buffers.
- Quoting/escaping writes segments directly to output, minimizing temporary objects.
- Tiny reusable scratch buffer (`_charScratch`) avoids per-char allocations.

Tradeoff:

- Async generic write path may still materialize temporary strings for formatted values.

### 8. Small hot-method inlining and local option hoisting

Code:

- `src/CsvToolkit.Core/Internal/CsvParser.cs` (`ReadChar`, `PushBack`)
- `src/CsvToolkit.Core/CsvRow.cs` (`GetFieldSpan`, `GetFieldMemory`)

Why it helps:

- Reduces call overhead in tight loops.
- Hoisting delimiter/quote/escape/trim flags into locals removes repeated property access.

Tradeoff:

- Micro-optimizations increase need for benchmark-backed justification.

### 9. Generated column name caching for dictionary mode

Code:

- `src/CsvToolkit.Core/CsvReader.cs` (`GetGeneratedColumnName`)

Why it helps:

- Prevents repeated `"Column{n}"` string construction in no-header / extra-column scenarios.

Tradeoff:

- Slight memory retention for generated key cache.

## Where Allocations Still Happen

Even with low-allocation internals, some APIs intentionally allocate:

- `CsvRow.GetFieldString(...)` and dictionary/dynamic APIs allocate strings.
- POCO mapping allocates record instances per row.
- `TryConvertFallback` may allocate temporary strings for unsupported built-in fast paths.

These are intentional for ergonomic APIs; span/memory APIs exist for allocation-sensitive call sites.

## Tuning Knobs

`CsvOptions` exposes practical controls:

- `CharBufferSize`: parser char-buffer size.
- `ByteBufferSize`: UTF-8 byte-buffer size for stream adapters.
- `TrimOptions`: trim behavior; can increase per-field CPU work.
- `DetectColumnCount`: safety check; disable only if malformed width is acceptable.
- `ReadMode`: `Strict` (throw fast) or `Lenient` (callback path).
- `CultureInfo`: affects number/date parsing behavior and cost.

Code: `src/CsvToolkit.Core/CsvOptions.cs`

## Latest Benchmark Snapshot

Run metadata:

- Date: `2026-02-10`
- Machine: `Apple M3 Pro`
- Runtime: `.NET 10.0.0`
- Rows: `100000`

Selected results:

- `CsvToolkitCore_WriteTyped_Stream`: `20.76 ms`, `25.97 MB`
- `CsvToolkitCore_ReadDictionary_Stream`: `26.78 ms`, `52.64 MB`
- `CsvToolkitCore_ReadTyped_SemicolonHighQuote`: `48.81 ms`, `19.2 MB`
- `CsvToolkitCore_ReadTyped_Stream`: `49.90 ms`, `19.2 MB`

Artifacts:

- `BenchmarkDotNet.Artifacts/results/CsvToolkit.Benchmarks.CsvReadWriteBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/CsvToolkit.Benchmarks.CsvReadWriteBenchmarks-report.csv`

## Decision Rule Used in This Project

Optimization is accepted only when:

1. There is a benchmark-visible gain on realistic CSV workloads.
2. Correctness is maintained for edge cases (quotes, delimiters, newlines, conversion).
3. API usability remains practical for both high-level and low-level consumers.
