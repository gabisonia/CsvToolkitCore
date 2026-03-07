# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

## [0.3.1] - 2026-03-07

### Added
- `Sep` benchmark coverage for common typed read/write scenarios in `benchmarks/CsvToolkit.Benchmarks`.

### Changed
- README benchmark summary updated for the `2026-03-07` benchmark run with `CsvToolkit.Core`, `CsvHelper`, and `Sep`.
- Tracked benchmark snapshots updated under `docs/benchmarks/`:
  - `docs/benchmarks/CsvReadWriteBenchmarks-2026-03-07.md`
  - `docs/benchmarks/CsvReadWriteBenchmarks-2026-03-07.csv`
- Parser hot paths optimized with single-delimiter fast paths, buffered scanning, and reduced delimiter fallback overhead.
- Typed read and typed write paths optimized with cached built-in materializers and writer fast paths.
- Converter-options writer paths optimized for common built-in types, with matching async write fast paths.

### Performance
- Full benchmark suite (`CsvReadWriteBenchmarks`, 38 benchmarks) run on `2026-03-07`.
- Typed read (`RowCount=100000`) improved to:
  - `CsvToolkitCore_ReadTyped_Stream`: `30.194 ms`, `9.30 MB`
  - `CsvHelper_ReadTyped_Stream`: `45.406 ms`, `36.72 MB`
  - `Sep_ReadTyped_Stream`: `21.504 ms`, `9.23 MB`
- Typed write (`RowCount=100000`) improved to:
  - `CsvToolkitCore_WriteTyped_Stream`: `21.855 ms`, `16.06 MB`
  - `CsvHelper_WriteTyped_Stream`: `23.322 ms`, `39.40 MB`
  - `Sep_WriteTyped_Stream`: `11.695 ms`, `16.01 MB`
- Typed write with converter options (`RowCount=100000`) improved to:
  - `CsvToolkitCore_WriteTyped_WithConverterOptions_Stream`: `17.481 ms`, `8.05 MB`
  - `CsvHelper_WriteTyped_WithConverterOptions_Stream`: `19.591 ms`, `26.60 MB`
  - `Sep_WriteTyped_WithConverterOptions_Stream`: `7.270 ms`, `9.93 MB`

## [0.3.0] - 2026-03-04

### Added
- Richer mapping features in fluent and attribute models:
  - `NameIndex` for duplicate headers
  - `Optional`
  - `Default`
  - `Constant`
  - `Validate`
- Converter option attributes:
  - `CsvNullValues`, `CsvTrueValues`, `CsvFalseValues`
  - `CsvFormats`, `CsvNumberStyles`, `CsvDateTimeStyles`, `CsvCulture`
- Constructor-based record materialization for immutable/constructor-only models.
- Reader ergonomics:
  - `GetField<T>(index)` and `GetField<T>(name, nameIndex)`
  - `ReadRecordAsync<T>()`
  - `GetRecords<T>()` and `GetRecordsAsync<T>()`
  - `CsvDataReader` adapter via `AsDataReader()`
- Writer ergonomics:
  - `WriteRecords(...)`
  - `WriteRecordsAsync(...)`
- Configuration callbacks and behaviors:
  - `MissingFieldFound`
  - `HeaderValidated`
  - `ReadingExceptionOccurred`
  - `PrepareHeaderForMatch`
  - CSV injection sanitization options

### Changed
- Typed read hot paths were optimized with cached read contexts, conversion plans, and fast built-in parsing paths.
- README benchmark section updated and a tracked benchmark snapshot added under `docs/benchmarks/`.

### Performance
- Full benchmark suite (`CsvReadWriteBenchmarks`, 28 benchmarks) run on `2026-03-04`.
- Typed read (`RowCount=100000`) improved to:
  - `CsvToolkitCore_ReadTyped_Stream`: `41.431 ms`, `9.30 MB`
  - `CsvHelper_ReadTyped_Stream`: `45.086 ms`, `36.72 MB`
- Complete benchmark artifacts are stored in:
  - `docs/benchmarks/CsvReadWriteBenchmarks-2026-03-04.md`
  - `docs/benchmarks/CsvReadWriteBenchmarks-2026-03-04.csv`
