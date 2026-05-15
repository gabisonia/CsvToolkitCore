using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using CsvToolkit.Core.Internal;
using CsvToolkit.Core.Mapping;
using CsvToolkit.Core.TypeConversion;

namespace CsvToolkit.Core;

/// <summary>
/// Streaming CSV reader supporting manual row access, dictionary/dynamic records, and POCO mapping.
/// </summary>
public sealed class CsvReader : IDisposable, IAsyncDisposable
{
    private static readonly object FailedMaterializationSentinel = new();
    private static readonly MethodInfo TryGetBuiltInFieldValueMethodDefinition = typeof(CsvReader).GetMethod(
        nameof(TryGetBuiltInFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TryGetBuiltInFieldValue method not found.");
    private static readonly MethodInfo TryGetStringFieldValueMethod = typeof(CsvReader).GetMethod(
        nameof(TryGetStringFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TryGetStringFieldValue method not found.");
    private static readonly MethodInfo TryGetInt32FieldValueMethod = typeof(CsvReader).GetMethod(
        nameof(TryGetInt32FieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TryGetInt32FieldValue method not found.");
    private static readonly MethodInfo TryGetNullableInt32FieldValueMethod = typeof(CsvReader).GetMethod(
        nameof(TryGetNullableInt32FieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TryGetNullableInt32FieldValue method not found.");
    private static readonly MethodInfo TryGetDecimalFieldValueMethod = typeof(CsvReader).GetMethod(
        nameof(TryGetDecimalFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TryGetDecimalFieldValue method not found.");
    private static readonly MethodInfo TryGetNullableDecimalFieldValueMethod = typeof(CsvReader).GetMethod(
        nameof(TryGetNullableDecimalFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TryGetNullableDecimalFieldValue method not found.");
    private static readonly MethodInfo TryGetDateTimeFieldValueMethod = typeof(CsvReader).GetMethod(
        nameof(TryGetDateTimeFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TryGetDateTimeFieldValue method not found.");
    private static readonly MethodInfo TryGetNullableDateTimeFieldValueMethod = typeof(CsvReader).GetMethod(
        nameof(TryGetNullableDateTimeFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TryGetNullableDateTimeFieldValue method not found.");
    private static readonly MethodInfo TryGetBooleanFieldValueMethod = typeof(CsvReader).GetMethod(
        nameof(TryGetBooleanFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TryGetBooleanFieldValue method not found.");
    private static readonly MethodInfo TryGetNullableBooleanFieldValueMethod = typeof(CsvReader).GetMethod(
        nameof(TryGetNullableBooleanFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TryGetNullableBooleanFieldValue method not found.");
    private static readonly MethodInfo BuildCompiledBuiltInRecordMaterializerMethodDefinition = typeof(CsvReader)
        .GetMethod(nameof(BuildCompiledBuiltInRecordMaterializer), BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("BuildCompiledBuiltInRecordMaterializer method not found.");
    private readonly CsvParser _parser;
    private readonly CsvMapRegistry _mapRegistry;
    private readonly Dictionary<Type, int[]> _memberIndexCache = new();
    private readonly Dictionary<Type, int[]> _assignableMemberIndexCache = new();
    private readonly Dictionary<Type, CsvValueConversionPlan[]> _memberConversionPlanCache = new();
    private readonly Dictionary<Type, CsvSimpleReadPlan?> _simpleReadPlanCache = new();
    private readonly Dictionary<Type, CsvRecordReadContext> _recordReadContextCache = new();
    private readonly Dictionary<Type, object> _compiledSimpleMaterializerCache = new();
    private readonly Dictionary<Type, Func<object>> _defaultConstructorFactoryCache = new();
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

    public string? DetectedDelimiter => _parser.DetectedDelimiter;

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

    public int ReadRows<TState>(TState state, CsvRowAction<TState> rowAction)
    {
        if (rowAction is null)
        {
            throw new ArgumentNullException(nameof(rowAction));
        }

        var count = 0;
        while (TryReadRow(out _))
        {
            rowAction(this, state);
            count++;
        }

        return count;
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

    public ReadOnlySpan<char> GetFieldSpan(string name, int nameIndex = 0)
    {
        var index = ResolveHeaderIndex(name, nameIndex);
        return GetFieldSpan(index);
    }

    public ReadOnlyMemory<char> GetFieldMemory(int index) => CurrentRow.GetFieldMemory(index);

    public ReadOnlyMemory<char> GetFieldMemory(string name, int nameIndex = 0)
    {
        var index = ResolveHeaderIndex(name, nameIndex);
        return GetFieldMemory(index);
    }

    public int GetFieldIndex(string name, int nameIndex = 0) => ResolveHeaderIndex(name, nameIndex);

    public string GetField(int index) => CurrentRow.GetFieldString(index);

    public string GetField(string name, int nameIndex = 0)
    {
        var index = ResolveHeaderIndex(name, nameIndex);
        return GetField(index);
    }

    public TField? GetField<TField>(int index)
    {
        var valueMemory = GetFieldMemory(index);
        var context = new CsvConverterContext(Options.CultureInfo, CurrentRow.RowIndex, index, null);
        if (CsvValueConverter.TryConvert(valueMemory.Span, typeof(TField), Options, null, null, context,
                out var converted))
        {
            return (TField?)converted;
        }

        HandleBadData(index, $"Failed to convert field at index {index}.", valueMemory);
        return default;
    }

    public TField? GetField<TField>(string name, int nameIndex = 0)
    {
        var index = ResolveHeaderIndex(name, nameIndex);
        return GetField<TField>(index);
    }

    public int GetInt32(int index)
    {
        if (TryGetInt32FieldValue(index, out var value))
        {
            return value;
        }

        HandleFieldConversionFailure(index, nameof(Int32));
        return default;
    }

    public int GetInt32(string name, int nameIndex = 0)
    {
        var index = ResolveHeaderIndex(name, nameIndex);
        return GetInt32(index);
    }

    public int? GetNullableInt32(int index)
    {
        if (TryGetNullableInt32FieldValue(index, out var value))
        {
            return value;
        }

        HandleFieldConversionFailure(index, $"{nameof(Int32)}?");
        return default;
    }

    public int? GetNullableInt32(string name, int nameIndex = 0)
    {
        var index = ResolveHeaderIndex(name, nameIndex);
        return GetNullableInt32(index);
    }

    public decimal GetDecimal(int index)
    {
        if (TryGetDecimalFieldValue(index, out var value))
        {
            return value;
        }

        HandleFieldConversionFailure(index, nameof(Decimal));
        return default;
    }

    public decimal GetDecimal(string name, int nameIndex = 0)
    {
        var index = ResolveHeaderIndex(name, nameIndex);
        return GetDecimal(index);
    }

    public decimal? GetNullableDecimal(int index)
    {
        if (TryGetNullableDecimalFieldValue(index, out var value))
        {
            return value;
        }

        HandleFieldConversionFailure(index, $"{nameof(Decimal)}?");
        return default;
    }

    public decimal? GetNullableDecimal(string name, int nameIndex = 0)
    {
        var index = ResolveHeaderIndex(name, nameIndex);
        return GetNullableDecimal(index);
    }

    public DateTime GetDateTime(int index)
    {
        if (TryGetDateTimeFieldValue(index, out var value))
        {
            return value;
        }

        HandleFieldConversionFailure(index, nameof(DateTime));
        return default;
    }

    public DateTime GetDateTime(string name, int nameIndex = 0)
    {
        var index = ResolveHeaderIndex(name, nameIndex);
        return GetDateTime(index);
    }

    public DateTime? GetNullableDateTime(int index)
    {
        if (TryGetNullableDateTimeFieldValue(index, out var value))
        {
            return value;
        }

        HandleFieldConversionFailure(index, $"{nameof(DateTime)}?");
        return default;
    }

    public DateTime? GetNullableDateTime(string name, int nameIndex = 0)
    {
        var index = ResolveHeaderIndex(name, nameIndex);
        return GetNullableDateTime(index);
    }

    public bool GetBoolean(int index)
    {
        if (TryGetBooleanFieldValue(index, out var value))
        {
            return value;
        }

        HandleFieldConversionFailure(index, nameof(Boolean));
        return default;
    }

    public bool GetBoolean(string name, int nameIndex = 0)
    {
        var index = ResolveHeaderIndex(name, nameIndex);
        return GetBoolean(index);
    }

    public bool? GetNullableBoolean(int index)
    {
        if (TryGetNullableBooleanFieldValue(index, out var value))
        {
            return value;
        }

        HandleFieldConversionFailure(index, $"{nameof(Boolean)}?");
        return default;
    }

    public bool? GetNullableBoolean(string name, int nameIndex = 0)
    {
        var index = ResolveHeaderIndex(name, nameIndex);
        return GetNullableBoolean(index);
    }

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
        var context = ResolveRecordReadContext(typeof(T));
        var map = context.Map;
        var indices = context.Indices;
        var assignableMemberIndices = context.AssignableMemberIndices;
        var conversionPlans = context.ConversionPlans;
        var constructorBoundMembers = Array.Empty<bool>();
        object boxed;

        var constructorBinding = context.ConstructorBinding;
        if (constructorBinding is not null)
        {
            boxed = CreateRecordUsingConstructor(map, constructorBinding, indices, conversionPlans,
                out constructorBoundMembers);
        }
        else
        {
            var compiledBuiltInRecordMaterializer = context.CompiledBuiltInRecordMaterializer;
            if (compiledBuiltInRecordMaterializer is not null)
            {
                var materialized = compiledBuiltInRecordMaterializer(this);
                if (!ReferenceEquals(materialized, FailedMaterializationSentinel))
                {
                    return (T)materialized!;
                }
            }

            boxed = context.DefaultConstructorFactory!();
            var simpleReadPlan = context.SimpleReadPlan;
            if (simpleReadPlan is not null)
            {
                if (TryPopulateUsingSimpleReadPlan(simpleReadPlan, boxed))
                {
                    return (T)boxed;
                }
            }
        }

        var hasConstructorBoundMembers = constructorBoundMembers.Length != 0;
        for (var i = 0; i < assignableMemberIndices.Length; i++)
        {
            var memberIndex = assignableMemberIndices[i];
            if (hasConstructorBoundMembers && constructorBoundMembers[memberIndex])
            {
                continue;
            }

            var member = map.Members[memberIndex];
            var fieldIndex = indices[memberIndex];
            if (!TryResolveMemberValue(member, fieldIndex, conversionPlans[memberIndex], out var converted,
                    out var valueMemory))
            {
                continue;
            }

            if (conversionPlans[memberIndex].UseBuiltInPath)
            {
                member.Setter!(boxed, converted);
            }
            else
            {
                AssignMemberValue(member, boxed, converted, fieldIndex, valueMemory);
            }
        }

        return (T)boxed;
    }

    public Dictionary<string, string?> GetCurrentRowDictionary()
    {
        return BuildDictionary(CurrentRow);
    }

    public IEnumerable<T> GetRecords<T>()
    {
        while (TryReadRecord<T>(out var record))
        {
            if (record is null)
            {
                continue;
            }

            yield return record;
        }
    }

    public async IAsyncEnumerable<T> GetRecordsAsync<T>(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return GetRecord<T>();
        }
    }

    public CsvDataReader AsDataReader(bool leaveOpen = false)
    {
        return new CsvDataReader(this, leaveOpen);
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
        _simpleReadPlanCache.Clear();
        _recordReadContextCache.Clear();
        _compiledSimpleMaterializerCache.Clear();
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
        _simpleReadPlanCache.Clear();
        _recordReadContextCache.Clear();
        _compiledSimpleMaterializerCache.Clear();
    }

    private CsvRecordReadContext ResolveRecordReadContext(Type type)
    {
        if (_recordReadContextCache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var map = _mapRegistry.GetOrCreate(type);
        var indices = ResolveFieldIndices(map);
        var assignableMemberIndices = ResolveAssignableMemberIndices(map);
        var conversionPlans = ResolveMemberConversionPlans(map);
        var constructorBinding = GetConstructorBinding(map);
        var simpleReadPlan = constructorBinding is null
            ? ResolveSimpleReadPlan(map, indices, conversionPlans, assignableMemberIndices)
            : null;
        var defaultConstructorFactory = constructorBinding is null
            ? ResolveDefaultConstructorFactory(type)
            : null;
        var compiledBuiltInRecordMaterializer = constructorBinding is null && simpleReadPlan is not null
            ? ResolveCompiledBuiltInRecordMaterializer(type, simpleReadPlan)
            : null;

        var context = new CsvRecordReadContext(
            map,
            indices,
            assignableMemberIndices,
            conversionPlans,
            constructorBinding,
            simpleReadPlan,
            defaultConstructorFactory,
            compiledBuiltInRecordMaterializer);
        _recordReadContextCache[type] = context;
        return context;
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

    private CsvValueConversionPlan[] ResolveMemberConversionPlans(Mapping.CsvTypeMap map)
    {
        if (_memberConversionPlanCache.TryGetValue(map.RecordType, out var cached))
        {
            return cached;
        }

        var plans = new CsvValueConversionPlan[map.Members.Length];
        for (var i = 0; i < map.Members.Length; i++)
        {
            var member = map.Members[i];
            if (member.Ignore)
            {
                continue;
            }

            plans[i] = CsvValueConverter.CreateConversionPlan(
                member.PropertyType,
                Options,
                member.Converter,
                member.ConverterOptions);
        }

        _memberConversionPlanCache[map.RecordType] = plans;
        return plans;
    }

    private int[] ResolveAssignableMemberIndices(Mapping.CsvTypeMap map)
    {
        if (_assignableMemberIndexCache.TryGetValue(map.RecordType, out var cached))
        {
            return cached;
        }

        var members = new List<int>(map.Members.Length);
        for (var i = 0; i < map.Members.Length; i++)
        {
            var member = map.Members[i];
            if (member.Ignore || member.Setter is null)
            {
                continue;
            }

            members.Add(i);
        }

        var indices = members.ToArray();
        _assignableMemberIndexCache[map.RecordType] = indices;
        return indices;
    }

    private CsvSimpleReadPlan? ResolveSimpleReadPlan(
        Mapping.CsvTypeMap map,
        int[] indices,
        CsvValueConversionPlan[] conversionPlans,
        int[] assignableMemberIndices)
    {
        if (_simpleReadPlanCache.TryGetValue(map.RecordType, out var cached))
        {
            return cached;
        }

        var members = new CsvSimpleReadMember[assignableMemberIndices.Length];
        var isBuiltInOnly = true;
        for (var i = 0; i < assignableMemberIndices.Length; i++)
        {
            var memberIndex = assignableMemberIndices[i];
            var member = map.Members[memberIndex];
            var fieldIndex = indices[memberIndex];

            if (fieldIndex < 0 ||
                member.HasConstant ||
                member.HasDefault ||
                member.Optional ||
                member.Validation is not null)
            {
                _simpleReadPlanCache[map.RecordType] = null;
                return null;
            }

            members[i] = new CsvSimpleReadMember(memberIndex, fieldIndex, conversionPlans[memberIndex], member);
            if (!conversionPlans[memberIndex].UseBuiltInPath)
            {
                isBuiltInOnly = false;
            }
        }

        var plan = new CsvSimpleReadPlan(members, isBuiltInOnly);
        _simpleReadPlanCache[map.RecordType] = plan;
        return plan;
    }

    private Func<CsvReader, object?>? ResolveCompiledBuiltInRecordMaterializer(Type type, CsvSimpleReadPlan plan)
    {
        if (type.IsValueType || !plan.IsBuiltInOnly)
        {
            return null;
        }

        if (_compiledSimpleMaterializerCache.TryGetValue(type, out var cached))
        {
            return (Func<CsvReader, object?>)cached;
        }

        var compiled = (Func<CsvReader, object?>)BuildCompiledBuiltInRecordMaterializerMethodDefinition
            .MakeGenericMethod(type)
            .Invoke(null, [plan])!;
        _compiledSimpleMaterializerCache[type] = compiled;
        return compiled;
    }

    private static Func<CsvReader, object?> BuildCompiledBuiltInRecordMaterializer<T>(CsvSimpleReadPlan plan)
    {
        var reader = Expression.Parameter(typeof(CsvReader), "reader");
        var target = Expression.Variable(typeof(T), "target");
        var expressions = new List<Expression>(plan.Members.Length * 3 + 2);
        var variables = new List<ParameterExpression>(plan.Members.Length + 1) { target };
        var returnTarget = Expression.Label(typeof(object), "return");

        Expression newTarget;
        if (typeof(T).IsValueType)
        {
            newTarget = Expression.Default(typeof(T));
        }
        else
        {
            var constructor = typeof(T).GetConstructor(Type.EmptyTypes)
                ?? throw new InvalidOperationException(
                    $"Type '{typeof(T).Name}' must have a public parameterless constructor.");
            newTarget = Expression.New(constructor);
        }

        expressions.Add(Expression.Assign(target, newTarget));

        for (var i = 0; i < plan.Members.Length; i++)
        {
            var member = plan.Members[i];
            var property = member.Member.Property;
            var propertyType = property.PropertyType;
            var valueVariable = Expression.Variable(propertyType, $"value{i}");
            variables.Add(valueVariable);

            var tryGetMethod = ResolveBuiltInFieldAccessorMethod(propertyType);
            var tryGetCall = Expression.Call(
                reader,
                tryGetMethod,
                Expression.Constant(member.FieldIndex),
                valueVariable);
            expressions.Add(Expression.IfThen(
                Expression.IsFalse(tryGetCall),
                Expression.Return(returnTarget, Expression.Constant(FailedMaterializationSentinel, typeof(object)))));

            Expression targetAccess = target;
            if (property.DeclaringType is not null && property.DeclaringType != typeof(T))
            {
                targetAccess = Expression.Convert(targetAccess, property.DeclaringType);
            }

            expressions.Add(Expression.Assign(Expression.Property(targetAccess, property), valueVariable));
        }

        expressions.Add(Expression.Label(returnTarget, Expression.Convert(target, typeof(object))));

        var body = Expression.Block(variables, expressions);
        return Expression.Lambda<Func<CsvReader, object?>>(body, reader).Compile();
    }

    private static MethodInfo ResolveBuiltInFieldAccessorMethod(Type propertyType)
    {
        if (propertyType == typeof(string))
        {
            return TryGetStringFieldValueMethod;
        }

        if (propertyType == typeof(int))
        {
            return TryGetInt32FieldValueMethod;
        }

        if (propertyType == typeof(int?))
        {
            return TryGetNullableInt32FieldValueMethod;
        }

        if (propertyType == typeof(decimal))
        {
            return TryGetDecimalFieldValueMethod;
        }

        if (propertyType == typeof(decimal?))
        {
            return TryGetNullableDecimalFieldValueMethod;
        }

        if (propertyType == typeof(DateTime))
        {
            return TryGetDateTimeFieldValueMethod;
        }

        if (propertyType == typeof(DateTime?))
        {
            return TryGetNullableDateTimeFieldValueMethod;
        }

        if (propertyType == typeof(bool))
        {
            return TryGetBooleanFieldValueMethod;
        }

        if (propertyType == typeof(bool?))
        {
            return TryGetNullableBooleanFieldValueMethod;
        }

        return TryGetBuiltInFieldValueMethodDefinition.MakeGenericMethod(propertyType);
    }

    private bool TryGetStringFieldValue(int fieldIndex, out string value)
    {
        if ((uint)fieldIndex >= (uint)CurrentRow.FieldCount)
        {
            value = string.Empty;
            return false;
        }

        value = CurrentRow.GetFieldMemoryUnchecked(fieldIndex).ToString();
        return true;
    }

    private bool TryGetInt32FieldValue(int fieldIndex, out int value)
    {
        if ((uint)fieldIndex >= (uint)CurrentRow.FieldCount)
        {
            value = default;
            return false;
        }

        return TryParseInt32BuiltIn(CurrentRow.GetFieldSpanUnchecked(fieldIndex), Options.CultureInfo, out value);
    }

    private bool TryGetNullableInt32FieldValue(int fieldIndex, out int? value)
    {
        if ((uint)fieldIndex >= (uint)CurrentRow.FieldCount)
        {
            value = default;
            return false;
        }

        var source = CurrentRow.GetFieldSpanUnchecked(fieldIndex);
        if (source.Length == 0)
        {
            value = null;
            return true;
        }

        if (TryParseInt32BuiltIn(source, Options.CultureInfo, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = default;
        return false;
    }

    private bool TryGetDecimalFieldValue(int fieldIndex, out decimal value)
    {
        if ((uint)fieldIndex >= (uint)CurrentRow.FieldCount)
        {
            value = default;
            return false;
        }

        return TryParseDecimalBuiltIn(CurrentRow.GetFieldSpanUnchecked(fieldIndex), Options.CultureInfo, out value);
    }

    private bool TryGetNullableDecimalFieldValue(int fieldIndex, out decimal? value)
    {
        if ((uint)fieldIndex >= (uint)CurrentRow.FieldCount)
        {
            value = default;
            return false;
        }

        var source = CurrentRow.GetFieldSpanUnchecked(fieldIndex);
        if (source.Length == 0)
        {
            value = null;
            return true;
        }

        if (TryParseDecimalBuiltIn(source, Options.CultureInfo, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = default;
        return false;
    }

    private bool TryGetDateTimeFieldValue(int fieldIndex, out DateTime value)
    {
        if ((uint)fieldIndex >= (uint)CurrentRow.FieldCount)
        {
            value = default;
            return false;
        }

        return TryParseDateTimeBuiltIn(CurrentRow.GetFieldSpanUnchecked(fieldIndex), Options.CultureInfo, out value);
    }

    private bool TryGetNullableDateTimeFieldValue(int fieldIndex, out DateTime? value)
    {
        if ((uint)fieldIndex >= (uint)CurrentRow.FieldCount)
        {
            value = default;
            return false;
        }

        var source = CurrentRow.GetFieldSpanUnchecked(fieldIndex);
        if (source.Length == 0)
        {
            value = null;
            return true;
        }

        if (TryParseDateTimeBuiltIn(source, Options.CultureInfo, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = default;
        return false;
    }

    private bool TryGetBooleanFieldValue(int fieldIndex, out bool value)
    {
        if ((uint)fieldIndex >= (uint)CurrentRow.FieldCount)
        {
            value = default;
            return false;
        }

        return TryParseBooleanBuiltIn(CurrentRow.GetFieldSpanUnchecked(fieldIndex), out value);
    }

    private bool TryGetNullableBooleanFieldValue(int fieldIndex, out bool? value)
    {
        if ((uint)fieldIndex >= (uint)CurrentRow.FieldCount)
        {
            value = default;
            return false;
        }

        var source = CurrentRow.GetFieldSpanUnchecked(fieldIndex);
        if (source.Length == 0)
        {
            value = null;
            return true;
        }

        if (TryParseBooleanBuiltIn(source, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = default;
        return false;
    }

    private bool TryGetBuiltInFieldValue<TValue>(int fieldIndex, out TValue value)
    {
        if ((uint)fieldIndex >= (uint)CurrentRow.FieldCount)
        {
            value = default!;
            return false;
        }

        var source = CurrentRow.GetFieldSpanUnchecked(fieldIndex);
        var valueType = typeof(TValue);
        var nullableType = Nullable.GetUnderlyingType(valueType);
        var effectiveType = nullableType ?? valueType;

        if (source.Length == 0 && (nullableType is not null || !effectiveType.IsValueType))
        {
            value = default!;
            return true;
        }

        var culture = Options.CultureInfo;

        if (effectiveType == typeof(string))
        {
            var text = source.ToString();
            if (valueType == typeof(string))
            {
                value = (TValue)(object)text;
                return true;
            }

            if (nullableType == typeof(string))
            {
                value = (TValue)(object)text;
                return true;
            }
        }

        if (effectiveType == typeof(int) &&
            TryParseInt32BuiltIn(source, culture, out var intValue) &&
            TryAssignParsedValue(intValue, nullableType, out value))
        {
            return true;
        }

        if (effectiveType == typeof(decimal) &&
            TryParseDecimalBuiltIn(source, culture, out var decimalValue) &&
            TryAssignParsedValue(decimalValue, nullableType, out value))
        {
            return true;
        }

        if (effectiveType == typeof(DateTime) &&
            TryParseDateTimeBuiltIn(source, culture, out var dateTimeValue) &&
            TryAssignParsedValue(dateTimeValue, nullableType, out value))
        {
            return true;
        }

        if (effectiveType == typeof(bool) &&
            TryParseBooleanBuiltIn(source, out var boolValue) &&
            TryAssignParsedValue(boolValue, nullableType, out value))
        {
            return true;
        }

        if (!CsvValueConverter.TryConvertBuiltInPath(source, valueType, culture, out var converted))
        {
            value = default!;
            return false;
        }

        if (converted is null)
        {
            value = default!;
            return true;
        }

        if (converted is TValue typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseInt32BuiltIn(ReadOnlySpan<char> source, CultureInfo culture, out int value)
    {
        if (ReferenceEquals(culture, CultureInfo.InvariantCulture) && TryParseInt32Invariant(source, out value))
        {
            return true;
        }

        return int.TryParse(source, NumberStyles.Integer, culture, out value);
    }

    private static bool TryParseInt32Invariant(ReadOnlySpan<char> source, out int value)
    {
        value = default;
        if (source.Length == 0)
        {
            return false;
        }

        var index = 0;
        var sign = 1;
        if (source[0] == '-')
        {
            sign = -1;
            index = 1;
        }
        else if (source[0] == '+')
        {
            index = 1;
        }

        if (index == source.Length)
        {
            return false;
        }

        var limit = sign < 0 ? 2147483648u : 2147483647u;
        uint result = 0;
        for (; index < source.Length; index++)
        {
            var digit = (uint)(source[index] - '0');
            if (digit > 9 || result > (limit - digit) / 10)
            {
                value = default;
                return false;
            }

            result = result * 10 + digit;
        }

        value = sign < 0 ? unchecked(-(int)result) : (int)result;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseDecimalBuiltIn(ReadOnlySpan<char> source, CultureInfo culture, out decimal value)
    {
        if (ReferenceEquals(culture, CultureInfo.InvariantCulture) && TryParseDecimalInvariant(source, out value))
        {
            return true;
        }

        return decimal.TryParse(source, NumberStyles.Number, culture, out value);
    }

    private static bool TryParseDecimalInvariant(ReadOnlySpan<char> source, out decimal value)
    {
        value = default;
        if (source.Length == 0)
        {
            return false;
        }

        var index = 0;
        var negative = false;
        if (source[0] == '-')
        {
            negative = true;
            index = 1;
        }
        else if (source[0] == '+')
        {
            index = 1;
        }

        if (index == source.Length)
        {
            return false;
        }

        ulong result = 0;
        var scale = 0;
        var seenDecimalPoint = false;
        var seenDigit = false;

        for (; index < source.Length; index++)
        {
            var ch = source[index];
            if (ch == '.')
            {
                if (seenDecimalPoint)
                {
                    return false;
                }

                seenDecimalPoint = true;
                continue;
            }

            var digit = (uint)(ch - '0');
            if (digit > 9)
            {
                return false;
            }

            if (result > (ulong.MaxValue - digit) / 10)
            {
                return false;
            }

            result = result * 10 + digit;
            seenDigit = true;
            if (seenDecimalPoint)
            {
                scale++;
                if (scale > 28)
                {
                    return false;
                }
            }
        }

        if (!seenDigit)
        {
            return false;
        }

        value = new decimal((int)result, (int)(result >> 32), 0, negative, (byte)scale);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryAssignParsedValue<TParsed, TValue>(TParsed parsed, Type? nullableType, out TValue value)
    {
        if (typeof(TValue) == typeof(TParsed))
        {
            value = (TValue)(object)parsed!;
            return true;
        }

        if (nullableType == typeof(TParsed))
        {
            value = (TValue)(object)parsed!;
            return true;
        }

        value = default!;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseBooleanBuiltIn(ReadOnlySpan<char> source, out bool value)
    {
        if (source.Length == 1)
        {
            if (source[0] == '1')
            {
                value = true;
                return true;
            }

            if (source[0] == '0')
            {
                value = false;
                return true;
            }
        }

        if (source.Equals("true".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (source.Equals("false".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        if (bool.TryParse(source, out value))
        {
            return true;
        }

        value = false;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseDateTimeBuiltIn(ReadOnlySpan<char> source, CultureInfo culture, out DateTime value)
    {
        if (ReferenceEquals(culture, CultureInfo.InvariantCulture) &&
            (TryParseRoundtripUtcDateTime(source, out value) ||
             DateTime.TryParseExact(source, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                 out value)))
        {
            return true;
        }

        return DateTime.TryParse(source, culture, DateTimeStyles.None, out value);
    }

    private static bool TryParseRoundtripUtcDateTime(ReadOnlySpan<char> source, out DateTime value)
    {
        value = default;
        if (source.Length != 28 ||
            source[4] != '-' ||
            source[7] != '-' ||
            source[10] != 'T' ||
            source[13] != ':' ||
            source[16] != ':' ||
            source[19] != '.' ||
            source[27] != 'Z')
        {
            return false;
        }

        if (!TryParseFixedDigits(source[..4], out var year) ||
            !TryParseFixedDigits(source.Slice(5, 2), out var month) ||
            !TryParseFixedDigits(source.Slice(8, 2), out var day) ||
            !TryParseFixedDigits(source.Slice(11, 2), out var hour) ||
            !TryParseFixedDigits(source.Slice(14, 2), out var minute) ||
            !TryParseFixedDigits(source.Slice(17, 2), out var second) ||
            !TryParseFixedDigits(source.Slice(20, 7), out var fraction))
        {
            return false;
        }

        try
        {
            value = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc)
                .AddTicks(fraction);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }
    }

    private static bool TryParseFixedDigits(ReadOnlySpan<char> source, out int value)
    {
        value = 0;
        for (var i = 0; i < source.Length; i++)
        {
            var digit = source[i] - '0';
            if ((uint)digit > 9)
            {
                value = default;
                return false;
            }

            value = value * 10 + digit;
        }

        return true;
    }

    private bool TryPopulateUsingSimpleReadPlan(CsvSimpleReadPlan plan, object target)
    {
        for (var i = 0; i < plan.Members.Length; i++)
        {
            var memberPlan = plan.Members[i];
            if (memberPlan.FieldIndex >= CurrentRow.FieldCount)
            {
                return false;
            }

            var valueMemory = CurrentRow.GetFieldMemory(memberPlan.FieldIndex);
            object? converted;

            if (memberPlan.ConversionPlan.UseBuiltInPath)
            {
                if (!CsvValueConverter.TryConvertBuiltInPath(
                        valueMemory.Span,
                        memberPlan.Member.PropertyType,
                        Options.CultureInfo,
                        out converted))
                {
                    return false;
                }
            }
            else
            {
                var context = new CsvConverterContext(
                    Options.CultureInfo,
                    CurrentRow.RowIndex,
                    memberPlan.FieldIndex,
                    memberPlan.Member.Name);
                if (!CsvValueConverter.TryConvert(valueMemory.Span, memberPlan.ConversionPlan, context,
                        out converted))
                {
                    return false;
                }
            }

            try
            {
                memberPlan.Member.Setter!(target, converted);
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private bool TryResolveMemberValue(
        Mapping.CsvPropertyMap member,
        int fieldIndex,
        in CsvValueConversionPlan conversionPlan,
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
        var convertedSuccessfully = conversionPlan.UseBuiltInPath
            ? CsvValueConverter.TryConvertBuiltInPath(valueMemory.Span, member.PropertyType, Options.CultureInfo,
                out converted)
            : CsvValueConverter.TryConvert(
                valueMemory.Span,
                conversionPlan,
                new CsvConverterContext(Options.CultureInfo, CurrentRow.RowIndex, fieldIndex, member.Name),
                out converted);
        if (!convertedSuccessfully)
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

    private void AssignMemberValue(
        Mapping.CsvPropertyMap member,
        object target,
        object? value,
        int fieldIndex,
        ReadOnlyMemory<char> valueMemory)
    {
        try
        {
            member.Setter!(target, value);
        }
        catch (Exception ex)
        {
            HandleBadData(fieldIndex, $"Failed to assign member '{member.Name}': {ex.Message}", valueMemory);
        }
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
        CsvValueConversionPlan[] conversionPlans,
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
                if (TryResolveMemberValue(member, fieldIndex, conversionPlans[memberIndex], out var value, out _))
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

    private Func<object> ResolveDefaultConstructorFactory(Type type)
    {
        if (_defaultConstructorFactoryCache.TryGetValue(type, out var factory))
        {
            return factory;
        }

        try
        {
            Func<object> compiledFactory;
            if (type.IsValueType)
            {
                var body = Expression.Convert(Expression.Default(type), typeof(object));
                compiledFactory = Expression.Lambda<Func<object>>(body).Compile();
            }
            else
            {
                var constructor = type.GetConstructor(Type.EmptyTypes);
                if (constructor is null)
                {
                    throw new MissingMethodException(type.FullName, ".ctor()");
                }

                var body = Expression.Convert(Expression.New(constructor), typeof(object));
                compiledFactory = Expression.Lambda<Func<object>>(body).Compile();
            }

            _defaultConstructorFactoryCache[type] = compiledFactory;
            return compiledFactory;
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

    private void HandleFieldConversionFailure(int fieldIndex, string targetType)
    {
        var rawField = (uint)fieldIndex < (uint)CurrentRow.FieldCount
            ? CurrentRow.GetFieldMemory(fieldIndex)
            : ReadOnlyMemory<char>.Empty;
        HandleBadData(fieldIndex, $"Failed to convert field at index {fieldIndex} to {targetType}.", rawField);
    }

    private int ResolveHeaderIndex(string name, int nameIndex)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Field name cannot be null or whitespace.", nameof(name));
        }

        EnsureHeaderInitialized();

        if (_headerLookup is null)
        {
            throw new InvalidOperationException("Cannot access field by name when header is disabled or not available.");
        }

        var prepared = PrepareHeaderForMatch(name, -1);
        if (!_headerLookup.TryGetValue(prepared, out var indices))
        {
            throw new KeyNotFoundException($"Header '{name}' was not found.");
        }

        if (nameIndex < 0 || nameIndex >= indices.Length)
        {
            throw new IndexOutOfRangeException(
                $"Header '{name}' does not have index {nameIndex}. Available count: {indices.Length}.");
        }

        return indices[nameIndex];
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

    private sealed class CsvSimpleReadPlan(CsvSimpleReadMember[] members, bool isBuiltInOnly)
    {
        public CsvSimpleReadMember[] Members { get; } = members;

        public bool IsBuiltInOnly { get; } = isBuiltInOnly;
    }

    private sealed class CsvRecordReadContext(
        Mapping.CsvTypeMap map,
        int[] indices,
        int[] assignableMemberIndices,
        CsvValueConversionPlan[] conversionPlans,
        CsvConstructorBinding? constructorBinding,
        CsvSimpleReadPlan? simpleReadPlan,
        Func<object>? defaultConstructorFactory,
        Func<CsvReader, object?>? compiledBuiltInRecordMaterializer)
    {
        public Mapping.CsvTypeMap Map { get; } = map;

        public int[] Indices { get; } = indices;

        public int[] AssignableMemberIndices { get; } = assignableMemberIndices;

        public CsvValueConversionPlan[] ConversionPlans { get; } = conversionPlans;

        public CsvConstructorBinding? ConstructorBinding { get; } = constructorBinding;

        public CsvSimpleReadPlan? SimpleReadPlan { get; } = simpleReadPlan;

        public Func<object>? DefaultConstructorFactory { get; } = defaultConstructorFactory;

        public Func<CsvReader, object?>? CompiledBuiltInRecordMaterializer { get; } =
            compiledBuiltInRecordMaterializer;
    }

    private readonly record struct CsvSimpleReadMember(
        int MemberIndex,
        int FieldIndex,
        CsvValueConversionPlan ConversionPlan,
        Mapping.CsvPropertyMap Member);
}
