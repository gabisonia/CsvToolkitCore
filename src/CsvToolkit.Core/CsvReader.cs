using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Reflection;
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
    private readonly Dictionary<Type, CsvConstructorBinding?> _constructorBindingCache = new();
    private bool _headerInitialized;
    private string[]? _headers;
    private Dictionary<string, int[]>? _headerLookup;
    private string[] _generatedColumnNames = [];
    private int? _expectedColumnCount;

    public CsvReader(TextReader reader, CsvOptions? options = null, CsvMapRegistry? mapRegistry = null,
        bool leaveOpen = false)
    {
        if (reader is null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        Options = (options ?? CsvOptions.Default).Clone();
        Options.Validate();
        _mapRegistry = mapRegistry ?? new CsvMapRegistry();
        _parser = new CsvParser(new TextReaderInput(reader, leaveOpen), Options);
    }

    public CsvReader(Stream stream, CsvOptions? options = null, CsvMapRegistry? mapRegistry = null,
        bool leaveOpen = false)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

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

        CurrentRow = row;
        ValidateColumnCount(row);
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

    public async ValueTask<Dictionary<string, string?>?> ReadDictionaryAsync(
        CancellationToken cancellationToken = default)
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

    public bool TryReadRecord<T>([NotNullWhen(true)] out T? record)
    {
        if (!TryReadRow(out _))
        {
            record = default;
            return false;
        }

        record = GetRecord<T>()!;
        return true;
    }

    public async ValueTask<T?> ReadRecordAsync<T>(CancellationToken cancellationToken = default)
    {
        if (!await ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return default;
        }

        return GetRecord<T>();
    }

    public T GetRecord<T>()
    {
        var type = typeof(T);
        var map = _mapRegistry.GetOrCreate(type);
        var indices = ResolveFieldIndices(map);
        var constructorBoundMembers = Array.Empty<bool>();
        object boxed;

        var constructorBinding = GetConstructorBinding(map);
        if (constructorBinding is not null)
        {
            boxed = CreateRecordUsingConstructor(map, constructorBinding, indices, out constructorBoundMembers);
        }
        else
        {
            boxed = CreateRecordWithDefaultConstructor(type);
        }

        for (var i = 0; i < map.Members.Length; i++)
        {
            var member = map.Members[i];
            if (member.Ignore || member.Setter is null)
            {
                continue;
            }

            if (constructorBoundMembers.Length != 0 && constructorBoundMembers[i])
            {
                continue;
            }

            var fieldIndex = indices[i];
            if (!TryResolveMemberValue(member, fieldIndex, out var converted, out var valueMemory))
            {
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
        Options.HeaderValidated?.Invoke(new CsvHeaderValidationContext(headerRow.RowIndex, headerRow.LineNumber, _headers));

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
        Options.HeaderValidated?.Invoke(new CsvHeaderValidationContext(headerRow.RowIndex, headerRow.LineNumber, _headers));

        if (Options.DetectColumnCount)
        {
            _expectedColumnCount = headerRow.FieldCount;
        }

        _memberIndexCache.Clear();
    }

    private Dictionary<string, int[]> BuildHeaderLookup(IReadOnlyList<string> headers)
    {
        var grouped = new Dictionary<string, List<int>>(headers.Count, Options.HeaderComparer);
        for (var i = 0; i < headers.Count; i++)
        {
            var preparedHeader = PrepareHeaderForMatch(headers[i], i);
            if (!grouped.TryGetValue(preparedHeader, out var indices))
            {
                indices = [];
                grouped[preparedHeader] = indices;
            }

            indices.Add(i);
        }

        var lookup = new Dictionary<string, int[]>(grouped.Count, Options.HeaderComparer);
        foreach (var pair in grouped)
        {
            lookup[pair.Key] = pair.Value.ToArray();
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
        HashSet<int>? usedOrdinals = null;
        if (_headerLookup is null)
        {
            usedOrdinals = [];
            for (var i = 0; i < map.Members.Length; i++)
            {
                var member = map.Members[i];
                if (member.Ignore || !member.Index.HasValue)
                {
                    continue;
                }

                usedOrdinals.Add(member.Index.Value);
            }
        }

        var fallbackIndex = 0;

        for (var i = 0; i < map.Members.Length; i++)
        {
            var member = map.Members[i];

            if (member.Ignore)
            {
                indices[i] = -1;
                continue;
            }

            if (member.Index.HasValue)
            {
                indices[i] = member.Index.Value;
                continue;
            }

            if (_headerLookup is not null &&
                _headerLookup.TryGetValue(PrepareHeaderForMatch(member.Name, -1), out var headerIndices))
            {
                var nameIndex = member.NameIndex ?? 0;
                if (nameIndex >= 0 && nameIndex < headerIndices.Length)
                {
                    indices[i] = headerIndices[nameIndex];
                    continue;
                }
            }

            if (_headerLookup is not null)
            {
                indices[i] = -1;
                continue;
            }

            while (usedOrdinals!.Contains(fallbackIndex))
            {
                fallbackIndex++;
            }

            indices[i] = fallbackIndex;
            usedOrdinals!.Add(fallbackIndex);
            fallbackIndex++;
        }

        _memberIndexCache[map.RecordType] = indices;
        return indices;
    }

    private bool TryResolveMemberValue(
        Mapping.CsvPropertyMap member,
        int fieldIndex,
        out object? converted,
        out ReadOnlyMemory<char> valueMemory)
    {
        valueMemory = ReadOnlyMemory<char>.Empty;
        converted = null;

        if (member.HasConstant)
        {
            converted = member.ConstantValue;
            return ValidateMemberValue(member, fieldIndex, ref converted, valueMemory);
        }

        if (fieldIndex < 0)
        {
            NotifyMissingField(member, -1, $"No matching column for member '{member.Name}'.");

            if (member.HasDefault)
            {
                converted = member.DefaultValue;
                return ValidateMemberValue(member, -1, ref converted, valueMemory);
            }

            if (member.Optional)
            {
                return false;
            }

            HandleBadData(-1, $"No matching column for member '{member.Name}'.", ReadOnlyMemory<char>.Empty);
            return false;
        }

        if (fieldIndex >= CurrentRow.FieldCount)
        {
            NotifyMissingField(member, fieldIndex, $"Missing field for member '{member.Name}'.");

            if (member.HasDefault)
            {
                converted = member.DefaultValue;
                return ValidateMemberValue(member, fieldIndex, ref converted, valueMemory);
            }

            if (member.Optional)
            {
                return false;
            }

            HandleBadData(fieldIndex, $"Missing field for member '{member.Name}'.", ReadOnlyMemory<char>.Empty);
            return false;
        }

        valueMemory = CurrentRow.GetFieldMemory(fieldIndex);
        var context = new CsvConverterContext(Options.CultureInfo, CurrentRow.RowIndex, fieldIndex, member.Name);
        if (!CsvValueConverter.TryConvert(valueMemory.Span, member.PropertyType, Options, member.Converter, context,
                out converted))
        {
            if (member.HasDefault)
            {
                converted = member.DefaultValue;
                return ValidateMemberValue(member, fieldIndex, ref converted, valueMemory);
            }

            if (member.Optional)
            {
                return false;
            }

            HandleBadData(fieldIndex, $"Failed to convert field '{member.Name}'.", valueMemory);
            return false;
        }

        return ValidateMemberValue(member, fieldIndex, ref converted, valueMemory);
    }

    private bool ValidateMemberValue(
        Mapping.CsvPropertyMap member,
        int fieldIndex,
        ref object? converted,
        ReadOnlyMemory<char> valueMemory)
    {
        if (member.Validation is null || member.Validation(converted))
        {
            return true;
        }

        if (member.HasDefault && member.Validation(member.DefaultValue))
        {
            converted = member.DefaultValue;
            return true;
        }

        var validationMessage = member.ValidationMessage ?? $"Validation failed for member '{member.Name}'.";
        HandleBadData(fieldIndex < 0 ? -1 : fieldIndex, validationMessage, valueMemory);
        return false;
    }

    private object CreateRecordUsingConstructor(
        Mapping.CsvTypeMap map,
        CsvConstructorBinding constructorBinding,
        int[] indices,
        out bool[] constructorBoundMembers)
    {
        var parameters = constructorBinding.Constructor.GetParameters();
        var args = new object?[parameters.Length];
        constructorBoundMembers = new bool[map.Members.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var memberIndex = constructorBinding.MemberIndices[i];
            if (memberIndex >= 0)
            {
                var member = map.Members[memberIndex];
                var fieldIndex = indices[memberIndex];
                if (TryResolveMemberValue(member, fieldIndex, out var value, out _))
                {
                    args[i] = value;
                }
                else if (constructorBinding.ParameterHasDefaultValue[i])
                {
                    args[i] = constructorBinding.ParameterDefaultValues[i];
                }
                else
                {
                    args[i] = GetDefaultValue(parameters[i].ParameterType);
                }

                constructorBoundMembers[memberIndex] = true;
                continue;
            }

            if (constructorBinding.ParameterHasDefaultValue[i])
            {
                args[i] = constructorBinding.ParameterDefaultValues[i];
                continue;
            }

            args[i] = GetDefaultValue(parameters[i].ParameterType);
        }

        try
        {
            return constructorBinding.Constructor.Invoke(args);
        }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException(
                $"Failed to construct '{map.RecordType.Name}' from CSV values: {ex.InnerException?.Message ?? ex.Message}",
                ex.InnerException ?? ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to construct '{map.RecordType.Name}' from CSV values: {ex.Message}",
                ex);
        }
    }

    private object CreateRecordWithDefaultConstructor(Type type)
    {
        try
        {
            return Activator.CreateInstance(type) ??
                   throw new InvalidOperationException($"Failed to create instance of '{type.Name}'.");
        }
        catch (MissingMethodException ex)
        {
            throw new InvalidOperationException(
                $"Type '{type.Name}' must have a public parameterless constructor or a bindable constructor.",
                ex);
        }
    }

    private CsvConstructorBinding? GetConstructorBinding(Mapping.CsvTypeMap map)
    {
        var type = map.RecordType;
        if (_constructorBindingCache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        if (type.GetConstructor(Type.EmptyTypes) is not null)
        {
            _constructorBindingCache[type] = null;
            return null;
        }

        CsvConstructorBinding? binding = null;
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(static c => c.GetParameters().Length);

        foreach (var constructor in constructors)
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length == 0)
            {
                continue;
            }

            var memberIndices = new int[parameters.Length];
            var hasDefaults = new bool[parameters.Length];
            var defaults = new object?[parameters.Length];
            var valid = true;

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var memberIndex = FindMemberIndexByPropertyName(map, parameter.Name);
                if (memberIndex >= 0)
                {
                    memberIndices[i] = memberIndex;
                    continue;
                }

                memberIndices[i] = -1;
                if (parameter.HasDefaultValue)
                {
                    hasDefaults[i] = true;
                    defaults[i] = parameter.DefaultValue;
                    continue;
                }

                valid = false;
                break;
            }

            if (!valid)
            {
                continue;
            }

            binding = new CsvConstructorBinding(constructor, memberIndices, hasDefaults, defaults);
            break;
        }

        _constructorBindingCache[type] = binding;
        return binding;
    }

    private static int FindMemberIndexByPropertyName(Mapping.CsvTypeMap map, string? parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return -1;
        }

        for (var i = 0; i < map.Members.Length; i++)
        {
            if (string.Equals(map.Members[i].Property.Name, parameterName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static object? GetDefaultValue(Type type)
    {
        if (!type.IsValueType)
        {
            return null;
        }

        return Activator.CreateInstance(type);
    }

    private string PrepareHeaderForMatch(string header, int index)
    {
        return Options.PrepareHeaderForMatch(header, index) ?? header;
    }

    private void NotifyMissingField(Mapping.CsvPropertyMap member, int fieldIndex, string message)
    {
        Options.MissingFieldFound?.Invoke(new CsvMissingFieldContext(
            CurrentRow.RowIndex,
            CurrentRow.LineNumber,
            fieldIndex,
            member.Name,
            message));
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
            var exception = new CsvException(message, CurrentRow.RowIndex, CurrentRow.LineNumber, fieldIndex);
            if (Options.ReadingExceptionOccurred?.Invoke(
                    new CsvReadingExceptionContext(exception, CurrentRow.RowIndex, CurrentRow.LineNumber, fieldIndex,
                        rawField)) == true)
            {
                return;
            }

            throw exception;
        }

        Options.BadDataFound?.Invoke(new CsvBadDataContext(CurrentRow.RowIndex, CurrentRow.LineNumber, fieldIndex,
            message, rawField));
    }

    private sealed class CsvConstructorBinding(
        ConstructorInfo constructor,
        int[] memberIndices,
        bool[] parameterHasDefaultValue,
        object?[] parameterDefaultValues)
    {
        public ConstructorInfo Constructor { get; } = constructor;

        public int[] MemberIndices { get; } = memberIndices;

        public bool[] ParameterHasDefaultValue { get; } = parameterHasDefaultValue;

        public object?[] ParameterDefaultValues { get; } = parameterDefaultValues;
    }
}
