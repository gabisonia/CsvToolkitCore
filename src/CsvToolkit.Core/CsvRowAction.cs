namespace CsvToolkit.Core;

/// <summary>
/// Processes a row from a <see cref="CsvReader"/> while caller-owned state carries cached indexes or output buffers.
/// </summary>
public delegate void CsvRowAction<in TState>(CsvReader reader, TState state);
