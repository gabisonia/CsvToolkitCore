using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using CsvToolkit.Core.Internal;
using CsvToolkit.Core.Mapping;
using CsvToolkit.Core.TypeConversion;

namespace CsvToolkit.Core;

/// <summary>
/// Streaming CSV reader supporting manual row access, dictionary/dynamic records, and POCO mapping.
/// </summary>
public sealed class CsvReader : IDisposable, IAsyncDisposable
{
    private readonly CsvParser _parser;
    private readonly CsvMapRegistry _mapRegistry;
    private readonly Dictionary<Type, int[]> _memberIndexCache = new();
    private bool _headerInitialized;
    private string[]? _headers;
    private Dictionary<string, int>? _headerLookup;
    private string[] _generatedColumnNames = [];
    private int? _expectedColumnCount;

    public CsvReader(TextReader reader, CsvOptions? options = null, CsvMapRegistry? mapRegistry = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Options = (options ?? CsvOptions.Default).Clone();
        Options.Validate();
        _mapRegistry = mapRegistry ?? new CsvMapRegistry();
        _parser = new CsvParser(new TextReaderInput(reader, leaveOpen), Options);
    }

    public CsvReader(Stream stream, CsvOptions? options = null, CsvMapRegistry? mapRegistry = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Options = (options ?? CsvOptions.Default).Clone();
        Options.Validate();
        _mapRegistry = mapRegistry ?? new CsvMapRegistry();
        _parser = new CsvParser(new Utf8StreamInput(stream, Options.ByteBufferSize, leaveOpen), Options);
    }

    public CsvOptions Options { get; }

    public CsvRow CurrentRow { get; private set; }

    public long RowIndex => CurrentRow.RowIndex;

    public long LineNumber => CurrentRow.LineNumber;

    public string? DetectedNewLine => _parser.DetectedNewLine;

    public int FieldCount => CurrentRow.FieldCount;

    public IReadOnlyList<string> Headers => _headers ?? [];

    public bool TryReadRow(out CsvRow row)
    {
        EnsureHeaderInitialized();

        if (!_parser.TryReadRow(out row))
        {
            CurrentRow = default;
            return false;
        }

        ValidateColumnCount(row);
        CurrentRow = row;
        return true;
    }

    public bool Read()
    {
        return TryReadRow(out _);
    }

    public async ValueTask<bool> ReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureHeaderInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (!await _parser.TryReadRowAsync(cancellationToken).ConfigureAwait(false))
        {
            CurrentRow = default;
            return false;
        }

        CurrentRow = _parser.CurrentRow;
        ValidateColumnCount(CurrentRow);
        return true;
    }

    public ReadOnlySpan<char> GetFieldSpan(int index) => CurrentRow.GetFieldSpan(index);

    public ReadOnlyMemory<char> GetFieldMemory(int index) => CurrentRow.GetFieldMemory(index);

    public string GetField(int index) => CurrentRow.GetFieldString(index);

    public bool TryReadDictionary([NotNullWhen(true)] out Dictionary<string, string?>? row)
    {
        if (!TryReadRow(out var csvRow))
        {
            row = null;
            return false;
        }

        row = BuildDictionary(csvRow);
        return true;
    }

    public async ValueTask<Dictionary<string, string?>?> ReadDictionaryAsync(CancellationToken cancellationToken = default)
    {
        if (!await ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return BuildDictionary(CurrentRow);
    }

    public bool TryReadDynamic([NotNullWhen(true)] out dynamic? row)
    {
        if (!TryReadDictionary(out Dictionary<string, string?>? dictionary))
        {
            row = null;
            return false;
        }

        IDictionary<string, object?> expando = new ExpandoObject();
        foreach (var pair in dictionary)
        {
            expando[pair.Key] = pair.Value;
        }

        row = expando;
        return true;
    }

    public bool TryReadRecord<T>([NotNullWhen(true)] out T? record) where T : new()
    {
        if (!TryReadRow(out _))
        {
            record = default;
            return false;
        }

        record = GetRecord<T>()!;
        return true;
    }

    public async ValueTask<T?> ReadRecordAsync<T>(CancellationToken cancellationToken = default) where T : new()
    {
        if (!await ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return default;
        }

        return GetRecord<T>();
    }

    public T GetRecord<T>() where T : new()
    {
        if (CurrentRow.FieldCount == 0)
        {
            return new T();
        }

        var type = typeof(T);
        var map = _mapRegistry.GetOrCreate(type);
        var indices = ResolveFieldIndices(map);

        var record = new T();
        object boxed = record;

        for (var i = 0; i < map.Members.Length; i++)
        {
            var member = map.Members[i];
            if (member.Ignore || member.Setter is null)
            {
                continue;
            }

            var fieldIndex = indices[i];
            if (fieldIndex < 0)
            {
                HandleBadData(-1, $"No matching column for member '{member.Name}'.", ReadOnlyMemory<char>.Empty);
                continue;
            }

            if (fieldIndex >= CurrentRow.FieldCount)
            {
                HandleBadData(fieldIndex, $"Missing field for member '{member.Name}'.", ReadOnlyMemory<char>.Empty);
                continue;
            }

            var valueMemory = CurrentRow.GetFieldMemory(fieldIndex);
            var context = new CsvConverterContext(Options.CultureInfo, CurrentRow.RowIndex, fieldIndex, member.Name);
            if (!CsvValueConverter.TryConvert(valueMemory.Span, member.PropertyType, Options, member.Converter, context, out var converted))
            {
                HandleBadData(fieldIndex, $"Failed to convert field '{member.Name}'.", valueMemory);
                continue;
            }

            try
            {
                member.Setter(boxed, converted);
            }
            catch (Exception ex)
            {
                HandleBadData(fieldIndex, $"Failed to assign member '{member.Name}': {ex.Message}", valueMemory);
            }
        }

        return (T)boxed;
    }

    public Dictionary<string, string?> GetCurrentRowDictionary()
    {
        return BuildDictionary(CurrentRow);
    }

    public void Dispose()
    {
        _parser.Dispose(); 
    }

    public ValueTask DisposeAsync()
    {
        return _parser.DisposeAsync();
    }

    private void EnsureHeaderInitialized()
    {
        if (_headerInitialized)
        {
            return;
        }

        _headerInitialized = true;
        _headers = [];

        if (!Options.HasHeader)
        {
            return;
        }

        if (!_parser.TryReadRow(out var headerRow))
        {
            return;
        }

        _headers = new string[headerRow.FieldCount];
        for (var i = 0; i < _headers.Length; i++)
        {
            _headers[i] = headerRow.GetFieldString(i);
        }

        _headerLookup = BuildHeaderLookup(_headers);

        if (Options.DetectColumnCount)
        {
            _expectedColumnCount = headerRow.FieldCount;
        }

        _memberIndexCache.Clear();
    }

    private async ValueTask EnsureHeaderInitializedAsync(CancellationToken cancellationToken)
    {
        if (_headerInitialized)
        {
            return;
        }

        _headerInitialized = true;
        _headers = [];

        if (!Options.HasHeader)
        {
            return;
        }

        if (!await _parser.TryReadRowAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var headerRow = _parser.CurrentRow;
        _headers = new string[headerRow.FieldCount];
        for (var i = 0; i < _headers.Length; i++)
        {
            _headers[i] = headerRow.GetFieldString(i);
        }

        _headerLookup = BuildHeaderLookup(_headers);

        if (Options.DetectColumnCount)
        {
            _expectedColumnCount = headerRow.FieldCount;
        }

        _memberIndexCache.Clear();
    }

    private Dictionary<string, int> BuildHeaderLookup(IReadOnlyList<string> headers)
    {
        var lookup = new Dictionary<string, int>(headers.Count, Options.HeaderComparer);
        for (var i = 0; i < headers.Count; i++)
        {
            lookup.TryAdd(headers[i], i);
        }

        return lookup;
    }

    private void ValidateColumnCount(CsvRow row)
    {
        if (!Options.DetectColumnCount)
        {
            return;
        }

        if (!_expectedColumnCount.HasValue)
        {
            _expectedColumnCount = row.FieldCount;
            return;
        }

        if (_expectedColumnCount.Value == row.FieldCount)
        {
            return;
        }

        HandleBadData(
            row.FieldCount,
            $"Column count mismatch. Expected {_expectedColumnCount.Value} columns but found {row.FieldCount}.",
            ReadOnlyMemory<char>.Empty);
    }

    private Dictionary<string, string?> BuildDictionary(CsvRow row)
    {
        var comparer = Options.HeaderComparer;
        var values = new Dictionary<string, string?>(Math.Max(row.FieldCount, _headers?.Length ?? 0), comparer);

        if (_headers is { Length: > 0 })
        {
            for (var i = 0; i < _headers.Length; i++)
            {
                var key = _headers[i];
                var value = i < row.FieldCount ? row.GetFieldString(i) : null;
                values[key] = value;
            }

            for (var i = _headers.Length; i < row.FieldCount; i++)
            {
                values[GetGeneratedColumnName(i)] = row.GetFieldString(i);
            }

            return values;
        }

        for (var i = 0; i < row.FieldCount; i++)
        {
            values[GetGeneratedColumnName(i)] = row.GetFieldString(i);
        }

        return values;
    }

    private int[] ResolveFieldIndices(Mapping.CsvTypeMap map)
    {
        if (_memberIndexCache.TryGetValue(map.RecordType, out var cached))
        {
            return cached;
        }

        var indices = new int[map.Members.Length];
        var fallbackIndex = 0;

        for (var i = 0; i < map.Members.Length; i++)
        {
            var member = map.Members[i];

            if (member.Ignore || member.Setter is null)
            {
                indices[i] = -1;
                continue;
            }

            if (member.Index.HasValue)
            {
                indices[i] = member.Index.Value;
                continue;
            }

            if (_headerLookup is not null && _headerLookup.TryGetValue(member.Name, out var headerIndex))
            {
                indices[i] = headerIndex;
                continue;
            }

            if (_headerLookup is not null)
            {
                indices[i] = -1;
                continue;
            }

            indices[i] = fallbackIndex;
            fallbackIndex++;
        }

        _memberIndexCache[map.RecordType] = indices;
        return indices;
    }

    private string GetGeneratedColumnName(int index)
    {
        if (index < _generatedColumnNames.Length)
        {
            return _generatedColumnNames[index];
        }

        var newSize = _generatedColumnNames.Length == 0 ? 8 : _generatedColumnNames.Length;
        while (newSize <= index)
        {
            newSize *= 2;
        }

        var names = new string[newSize];
        _generatedColumnNames.AsSpan().CopyTo(names);

        for (var i = _generatedColumnNames.Length; i < names.Length; i++)
        {
            names[i] = $"Column{i}";
        }

        _generatedColumnNames = names;
        return _generatedColumnNames[index];
    }

    private void HandleBadData(int fieldIndex, string message, ReadOnlyMemory<char> rawField)
    {
        if (Options.ReadMode == CsvReadMode.Strict)
        {
            throw new CsvException(message, CurrentRow.RowIndex, CurrentRow.LineNumber, fieldIndex);
        }

        Options.BadDataFound?.Invoke(new CsvBadDataContext(CurrentRow.RowIndex, CurrentRow.LineNumber, fieldIndex, message, rawField));
    }
}
