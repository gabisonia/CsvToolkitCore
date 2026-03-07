using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using CsvToolkit.Core.Internal;
using CsvToolkit.Core.Mapping;
using CsvToolkit.Core.TypeConversion;

namespace CsvToolkit.Core;

/// <summary>
/// Streaming CSV writer with low-allocation field writing and POCO mapping support.
/// </summary>
public sealed class CsvWriter : IDisposable, IAsyncDisposable
{
    private static readonly byte[] TrueUtf8Bytes = [(byte)'T', (byte)'r', (byte)'u', (byte)'e'];
    private static readonly byte[] FalseUtf8Bytes = [(byte)'F', (byte)'a', (byte)'l', (byte)'s', (byte)'e'];
    private static readonly MethodInfo WriteStringFieldValueMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteStringFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteStringFieldValue method not found.");
    private static readonly MethodInfo WriteStringFieldValueWithOptionsMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteStringFieldValueWithOptions),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteStringFieldValueWithOptions method not found.");
    private static readonly MethodInfo WriteInt32FieldValueMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteInt32FieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteInt32FieldValue method not found.");
    private static readonly MethodInfo WriteInt32FieldValueWithOptionsMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteInt32FieldValueWithOptions),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteInt32FieldValueWithOptions method not found.");
    private static readonly MethodInfo WriteNullableInt32FieldValueMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteNullableInt32FieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteNullableInt32FieldValue method not found.");
    private static readonly MethodInfo WriteNullableInt32FieldValueWithOptionsMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteNullableInt32FieldValueWithOptions),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteNullableInt32FieldValueWithOptions method not found.");
    private static readonly MethodInfo WriteDecimalFieldValueMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteDecimalFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteDecimalFieldValue method not found.");
    private static readonly MethodInfo WriteDecimalFieldValueWithOptionsMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteDecimalFieldValueWithOptions),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteDecimalFieldValueWithOptions method not found.");
    private static readonly MethodInfo WriteNullableDecimalFieldValueMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteNullableDecimalFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteNullableDecimalFieldValue method not found.");
    private static readonly MethodInfo WriteNullableDecimalFieldValueWithOptionsMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteNullableDecimalFieldValueWithOptions),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteNullableDecimalFieldValueWithOptions method not found.");
    private static readonly MethodInfo WriteDateTimeFieldValueMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteDateTimeFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteDateTimeFieldValue method not found.");
    private static readonly MethodInfo WriteDateTimeFieldValueWithOptionsMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteDateTimeFieldValueWithOptions),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteDateTimeFieldValueWithOptions method not found.");
    private static readonly MethodInfo WriteNullableDateTimeFieldValueMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteNullableDateTimeFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteNullableDateTimeFieldValue method not found.");
    private static readonly MethodInfo WriteNullableDateTimeFieldValueWithOptionsMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteNullableDateTimeFieldValueWithOptions),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteNullableDateTimeFieldValueWithOptions method not found.");
    private static readonly MethodInfo WriteBooleanFieldValueMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteBooleanFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteBooleanFieldValue method not found.");
    private static readonly MethodInfo WriteBooleanFieldValueWithOptionsMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteBooleanFieldValueWithOptions),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteBooleanFieldValueWithOptions method not found.");
    private static readonly MethodInfo WriteNullableBooleanFieldValueMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteNullableBooleanFieldValue),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteNullableBooleanFieldValue method not found.");
    private static readonly MethodInfo WriteNullableBooleanFieldValueWithOptionsMethod = typeof(CsvWriter).GetMethod(
        nameof(WriteNullableBooleanFieldValueWithOptions),
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("WriteNullableBooleanFieldValueWithOptions method not found.");
    private readonly ICsvCharOutput _output;
    private readonly Utf8StreamOutput? _utf8Output;
    private readonly CsvMapRegistry _mapRegistry;
    private readonly Dictionary<Type, int[]> _writableMemberIndexCache = new();
    private readonly Dictionary<Type, CsvValueConversionPlan[]> _memberFormattingPlanCache = new();
    private readonly Dictionary<Type, CsvSimpleWritePlan?> _simpleWritePlanCache = new();
    private readonly Dictionary<Type, object> _compiledSimpleWriterCache = new();
    private readonly Dictionary<Type, object> _compiledAsyncSimpleWriterCache = new();
    private readonly char[] _charScratch = new char[2];
    private readonly byte[]? _delimiterUtf8;
    private readonly byte[]? _newLineUtf8;
    private bool _firstField = true;
    private int _fieldIndex;

    public CsvWriter(TextWriter writer, CsvOptions? options = null, CsvMapRegistry? mapRegistry = null,
        bool leaveOpen = false)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        Options = (options ?? CsvOptions.Default).Clone();
        Options.Validate();
        _mapRegistry = mapRegistry ?? new CsvMapRegistry();
        _output = new TextWriterOutput(writer, leaveOpen);
        _utf8Output = null;
        _delimiterUtf8 = null;
        _newLineUtf8 = null;
    }

    public CsvWriter(Stream stream, CsvOptions? options = null, CsvMapRegistry? mapRegistry = null,
        bool leaveOpen = false)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        Options = (options ?? CsvOptions.Default).Clone();
        Options.Validate();
        _mapRegistry = mapRegistry ?? new CsvMapRegistry();
        _utf8Output = new Utf8StreamOutput(stream, Options.ByteBufferSize, leaveOpen);
        _output = _utf8Output;
        _delimiterUtf8 = Encoding.UTF8.GetBytes(Options.DelimiterString);
        _newLineUtf8 = Encoding.UTF8.GetBytes(Options.NewLine ?? Environment.NewLine);
    }

    private CsvOptions Options { get; }

    private long RowIndex { get; set; }

    public void WriteHeader<T>()
    {
        var map = _mapRegistry.GetOrCreate(typeof(T));
        foreach (var member in map.Members)
        {
            if (member.Ignore)
            {
                continue;
            }

            WriteField(member.Name.AsSpan());
        }

        NextRecord();
    }

    public async ValueTask WriteHeaderAsync<T>(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var map = _mapRegistry.GetOrCreate(typeof(T));
        foreach (var member in map.Members)
        {
            if (member.Ignore)
            {
                continue;
            }

            await WriteFieldCoreAsync(member.Name.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await NextRecordAsync(cancellationToken).ConfigureAwait(false);
    }

    private void WriteField(ReadOnlySpan<char> value)
    {
        if (TrySanitizeForInjection(value, out var sanitized))
        {
            value = sanitized.AsSpan();
        }

        WriteDelimiterIfNeeded();

        if (NeedsQuoting(value))
        {
            WriteChar(Options.Quote);
            WriteEscaped(value);
            WriteChar(Options.Quote);
        }
        else
        {
            _output.Write(value);
        }

        _firstField = false;
        _fieldIndex++;
    }

    private void WriteStringFieldValue(string? value)
    {
        WriteField(value.AsSpan());
    }

    private void WriteStringFieldValueWithOptions(string? value, CsvTypeConverterOptions? converterOptions)
    {
        if (value is not null)
        {
            WriteField(value.AsSpan());
            return;
        }

        WriteNullTokenOrEmpty(converterOptions);
    }

    private void WriteInt32FieldValue(int value)
    {
        if (TryWriteUtf8FormattedInt32(value, format: null, Options.CultureInfo))
        {
            return;
        }

        Span<char> buffer = stackalloc char[32];
        if (value.TryFormat(buffer, out var written, default, Options.CultureInfo))
        {
            WriteField(buffer[..written]);
            return;
        }

        WriteField(value.ToString(null, Options.CultureInfo).AsSpan());
    }

    private void WriteInt32FieldValueWithOptions(int value, CsvTypeConverterOptions? converterOptions)
    {
        WriteFormattedInt32(value, converterOptions);
    }

    private void WriteNullableInt32FieldValue(int? value)
    {
        if (value.HasValue)
        {
            WriteInt32FieldValue(value.Value);
            return;
        }

        WriteField(ReadOnlySpan<char>.Empty);
    }

    private void WriteNullableInt32FieldValueWithOptions(int? value, CsvTypeConverterOptions? converterOptions)
    {
        if (value.HasValue)
        {
            WriteFormattedInt32(value.Value, converterOptions);
            return;
        }

        WriteNullTokenOrEmpty(converterOptions);
    }

    private void WriteDecimalFieldValue(decimal value)
    {
        if (TryWriteUtf8FormattedDecimal(value, format: null, Options.CultureInfo))
        {
            return;
        }

        Span<char> buffer = stackalloc char[64];
        if (value.TryFormat(buffer, out var written, default, Options.CultureInfo))
        {
            WriteField(buffer[..written]);
            return;
        }

        WriteField(value.ToString(null, Options.CultureInfo).AsSpan());
    }

    private void WriteDecimalFieldValueWithOptions(decimal value, CsvTypeConverterOptions? converterOptions)
    {
        WriteFormattedDecimal(value, converterOptions);
    }

    private void WriteNullableDecimalFieldValue(decimal? value)
    {
        if (value.HasValue)
        {
            WriteDecimalFieldValue(value.Value);
            return;
        }

        WriteField(ReadOnlySpan<char>.Empty);
    }

    private void WriteNullableDecimalFieldValueWithOptions(decimal? value, CsvTypeConverterOptions? converterOptions)
    {
        if (value.HasValue)
        {
            WriteFormattedDecimal(value.Value, converterOptions);
            return;
        }

        WriteNullTokenOrEmpty(converterOptions);
    }

    private void WriteDateTimeFieldValue(DateTime value)
    {
        if (TryWriteUtf8FormattedDateTime(value, format: null, Options.CultureInfo))
        {
            return;
        }

        Span<char> buffer = stackalloc char[128];
        if (value.TryFormat(buffer, out var written, default, Options.CultureInfo))
        {
            WriteField(buffer[..written]);
            return;
        }

        WriteField(value.ToString(null, Options.CultureInfo).AsSpan());
    }

    private void WriteDateTimeFieldValueWithOptions(DateTime value, CsvTypeConverterOptions? converterOptions)
    {
        WriteFormattedDateTime(value, converterOptions);
    }

    private void WriteNullableDateTimeFieldValue(DateTime? value)
    {
        if (value.HasValue)
        {
            WriteDateTimeFieldValue(value.Value);
            return;
        }

        WriteField(ReadOnlySpan<char>.Empty);
    }

    private void WriteNullableDateTimeFieldValueWithOptions(DateTime? value, CsvTypeConverterOptions? converterOptions)
    {
        if (value.HasValue)
        {
            WriteFormattedDateTime(value.Value, converterOptions);
            return;
        }

        WriteNullTokenOrEmpty(converterOptions);
    }

    private void WriteBooleanFieldValue(bool value)
    {
        if (TryWriteUtf8Field(value ? TrueUtf8Bytes : FalseUtf8Bytes))
        {
            return;
        }

        if (value)
        {
            WriteField("True".AsSpan());
            return;
        }

        WriteField("False".AsSpan());
    }

    private void WriteBooleanFieldValueWithOptions(bool value, CsvTypeConverterOptions? converterOptions)
    {
        if (value)
        {
            if (converterOptions is { TrueValues.Count: > 0 })
            {
                if (TryWriteUtf8AsciiField(converterOptions.TrueValues[0]))
                {
                    return;
                }

                WriteField(converterOptions.TrueValues[0].AsSpan());
                return;
            }

            if (TryWriteUtf8Field(TrueUtf8Bytes))
            {
                return;
            }

            WriteField("True".AsSpan());
            return;
        }

        if (converterOptions is { FalseValues.Count: > 0 })
        {
            if (TryWriteUtf8AsciiField(converterOptions.FalseValues[0]))
            {
                return;
            }

            WriteField(converterOptions.FalseValues[0].AsSpan());
            return;
        }

        if (TryWriteUtf8Field(FalseUtf8Bytes))
        {
            return;
        }

        WriteField("False".AsSpan());
    }

    private ValueTask WriteStringFieldValueAsync(string? value, CsvTypeConverterOptions? converterOptions,
        CancellationToken cancellationToken)
    {
        if (value is not null)
        {
            return WriteFieldCoreAsync(value.AsMemory(), cancellationToken);
        }

        return WriteNullTokenOrEmptyAsync(converterOptions, cancellationToken);
    }

    private ValueTask WriteInt32FieldValueAsync(int value, CsvTypeConverterOptions? converterOptions,
        CancellationToken cancellationToken)
    {
        return WriteFormattedInt32Async(value, converterOptions, cancellationToken);
    }

    private ValueTask WriteNullableInt32FieldValueAsync(int? value, CsvTypeConverterOptions? converterOptions,
        CancellationToken cancellationToken)
    {
        return value.HasValue
            ? WriteFormattedInt32Async(value.Value, converterOptions, cancellationToken)
            : WriteNullTokenOrEmptyAsync(converterOptions, cancellationToken);
    }

    private ValueTask WriteDecimalFieldValueAsync(decimal value, CsvTypeConverterOptions? converterOptions,
        CancellationToken cancellationToken)
    {
        return WriteFormattedDecimalAsync(value, converterOptions, cancellationToken);
    }

    private ValueTask WriteNullableDecimalFieldValueAsync(decimal? value, CsvTypeConverterOptions? converterOptions,
        CancellationToken cancellationToken)
    {
        return value.HasValue
            ? WriteFormattedDecimalAsync(value.Value, converterOptions, cancellationToken)
            : WriteNullTokenOrEmptyAsync(converterOptions, cancellationToken);
    }

    private ValueTask WriteDateTimeFieldValueAsync(DateTime value, CsvTypeConverterOptions? converterOptions,
        CancellationToken cancellationToken)
    {
        return WriteFormattedDateTimeAsync(value, converterOptions, cancellationToken);
    }

    private ValueTask WriteNullableDateTimeFieldValueAsync(DateTime? value, CsvTypeConverterOptions? converterOptions,
        CancellationToken cancellationToken)
    {
        return value.HasValue
            ? WriteFormattedDateTimeAsync(value.Value, converterOptions, cancellationToken)
            : WriteNullTokenOrEmptyAsync(converterOptions, cancellationToken);
    }

    private ValueTask WriteBooleanFieldValueAsync(bool value, CsvTypeConverterOptions? converterOptions,
        CancellationToken cancellationToken)
    {
        if (value)
        {
            if (converterOptions is { TrueValues.Count: > 0 })
            {
                return WriteFieldCoreAsync(converterOptions.TrueValues[0].AsMemory(), cancellationToken);
            }

            return WriteFieldCoreAsync("True".AsMemory(), cancellationToken);
        }

        if (converterOptions is { FalseValues.Count: > 0 })
        {
            return WriteFieldCoreAsync(converterOptions.FalseValues[0].AsMemory(), cancellationToken);
        }

        return WriteFieldCoreAsync("False".AsMemory(), cancellationToken);
    }

    private ValueTask WriteNullableBooleanFieldValueAsync(bool? value, CsvTypeConverterOptions? converterOptions,
        CancellationToken cancellationToken)
    {
        return value.HasValue
            ? WriteBooleanFieldValueAsync(value.Value, converterOptions, cancellationToken)
            : WriteNullTokenOrEmptyAsync(converterOptions, cancellationToken);
    }

    private void WriteNullableBooleanFieldValue(bool? value)
    {
        if (value.HasValue)
        {
            WriteBooleanFieldValue(value.Value);
            return;
        }

        WriteField(ReadOnlySpan<char>.Empty);
    }

    private void WriteNullableBooleanFieldValueWithOptions(bool? value, CsvTypeConverterOptions? converterOptions)
    {
        if (value.HasValue)
        {
            WriteBooleanFieldValueWithOptions(value.Value, converterOptions);
            return;
        }

        WriteNullTokenOrEmpty(converterOptions);
    }

    public ValueTask WriteFieldAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken = default)
    {
        return WriteFieldCoreAsync(value, cancellationToken);
    }

    public void WriteField<T>(T value)
    {
        if (value is null)
        {
            WriteField(ReadOnlySpan<char>.Empty);
            return;
        }

        if (value is string stringValue)
        {
            WriteField(stringValue.AsSpan());
            return;
        }

        if (value is char c)
        {
            Span<char> span = stackalloc char[1];
            span[0] = c;
            WriteField(span);
            return;
        }

        if (value is IFormattable formattable)
        {
            var formattedValue = formattable.ToString(null, Options.CultureInfo) ?? string.Empty;
            WriteField(formattedValue.AsSpan());
            return;
        }

        var context = new CsvConverterContext(Options.CultureInfo, RowIndex, _fieldIndex, null);
        var formatted = CsvValueConverter.FormatToString(value, value.GetType(), Options, null, null, context);
        WriteField(formatted.AsSpan());
    }

    public async ValueTask WriteFieldAsync<T>(T value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (value is null)
        {
            await WriteFieldCoreAsync(ReadOnlyMemory<char>.Empty, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (value is string stringValue)
        {
            await WriteFieldCoreAsync(stringValue.AsMemory(), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (value is char c)
        {
            await WriteFieldCoreAsync(c.ToString().AsMemory(), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (value is IFormattable formattable)
        {
            var formattedValue = formattable.ToString(null, Options.CultureInfo) ?? string.Empty;
            await WriteFieldCoreAsync(formattedValue.AsMemory(), cancellationToken).ConfigureAwait(false);
            return;
        }

        var context = new CsvConverterContext(Options.CultureInfo, RowIndex, _fieldIndex, null);
        var formatted = CsvValueConverter.FormatToString(value, value.GetType(), Options, null, null, context);
        await WriteFieldCoreAsync(formatted.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    public void NextRecord()
    {
        if (_utf8Output is not null)
        {
            _utf8Output.WriteUtf8(_newLineUtf8!);
        }
        else
        {
            var newLine = Options.NewLine ?? Environment.NewLine;
            _output.Write(newLine.AsSpan());
        }

        _firstField = true;
        _fieldIndex = 0;
        RowIndex++;
    }

    public async ValueTask NextRecordAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_utf8Output is not null)
        {
            await _utf8Output.WriteUtf8Async(_newLineUtf8!, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var newLine = Options.NewLine ?? Environment.NewLine;
            await _output.WriteAsync(newLine.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        _firstField = true;
        _fieldIndex = 0;
        RowIndex++;
    }

    public void WriteRecord<T>(T record)
    {
        if (record is null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        var map = _mapRegistry.GetOrCreate(typeof(T));
        var writableMemberIndices = ResolveWritableMemberIndices(map);
        var formattingPlans = ResolveMemberFormattingPlans(map);
        var simpleWritePlan = ResolveSimpleWritePlan(map, writableMemberIndices, formattingPlans);
        if (simpleWritePlan is not null)
        {
            var compiledWriter = ResolveCompiledSimpleWriter<T>(simpleWritePlan);
            if (compiledWriter is not null)
            {
                compiledWriter(this, record);
                NextRecord();
                return;
            }
        }

        object boxed = record;

        for (var i = 0; i < writableMemberIndices.Length; i++)
        {
            var memberIndex = writableMemberIndices[i];
            var member = map.Members[memberIndex];

            var value = member.HasConstant ? member.ConstantValue : member.Getter!(boxed);
            if (value is null && member.HasDefault)
            {
                value = member.DefaultValue;
            }

            if (member.Validation is not null && !member.Validation(value))
            {
                var message = member.ValidationMessage ?? $"Validation failed for member '{member.Name}'.";
                throw new InvalidOperationException(message);
            }

            var formatted = formattingPlans[memberIndex].UseBuiltInPath
                ? CsvValueConverter.FormatToStringBuiltInPath(value, Options.CultureInfo)
                : CsvValueConverter.FormatToString(
                    value,
                    formattingPlans[memberIndex],
                    new CsvConverterContext(Options.CultureInfo, RowIndex, _fieldIndex, member.Name));
            WriteField(formatted.AsSpan());
        }

        NextRecord();
    }

    public async ValueTask WriteRecordAsync<T>(T record, CancellationToken cancellationToken = default)
    {
        if (record is null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var map = _mapRegistry.GetOrCreate(typeof(T));
        var writableMemberIndices = ResolveWritableMemberIndices(map);
        var formattingPlans = ResolveMemberFormattingPlans(map);
        var simpleWritePlan = ResolveSimpleWritePlan(map, writableMemberIndices, formattingPlans);
        if (simpleWritePlan is not null)
        {
            var compiledAsyncWriter = ResolveCompiledAsyncSimpleWriter<T>(simpleWritePlan);
            if (compiledAsyncWriter is not null)
            {
                await compiledAsyncWriter(this, record, cancellationToken).ConfigureAwait(false);
                await NextRecordAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        object boxed = record;

        for (var i = 0; i < writableMemberIndices.Length; i++)
        {
            var memberIndex = writableMemberIndices[i];
            var member = map.Members[memberIndex];

            var value = member.HasConstant ? member.ConstantValue : member.Getter!(boxed);
            if (value is null && member.HasDefault)
            {
                value = member.DefaultValue;
            }

            if (member.Validation is not null && !member.Validation(value))
            {
                var message = member.ValidationMessage ?? $"Validation failed for member '{member.Name}'.";
                throw new InvalidOperationException(message);
            }

            var formatted = formattingPlans[memberIndex].UseBuiltInPath
                ? CsvValueConverter.FormatToStringBuiltInPath(value, Options.CultureInfo)
                : CsvValueConverter.FormatToString(
                    value,
                    formattingPlans[memberIndex],
                    new CsvConverterContext(Options.CultureInfo, RowIndex, _fieldIndex, member.Name));
            await WriteFieldCoreAsync(formatted.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await NextRecordAsync(cancellationToken).ConfigureAwait(false);
    }

    public void WriteRecords<T>(IEnumerable<T> records, bool writeHeader = false)
    {
        if (records is null)
        {
            throw new ArgumentNullException(nameof(records));
        }

        if (writeHeader)
        {
            WriteHeader<T>();
        }

        foreach (var record in records)
        {
            WriteRecord(record!);
        }
    }

    public async ValueTask WriteRecordsAsync<T>(
        IEnumerable<T> records,
        bool writeHeader = false,
        CancellationToken cancellationToken = default)
    {
        if (records is null)
        {
            throw new ArgumentNullException(nameof(records));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (writeHeader)
        {
            await WriteHeaderAsync<T>(cancellationToken).ConfigureAwait(false);
        }

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteRecordAsync(record!, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask WriteRecordsAsync<T>(
        IAsyncEnumerable<T> records,
        bool writeHeader = false,
        CancellationToken cancellationToken = default)
    {
        if (records is null)
        {
            throw new ArgumentNullException(nameof(records));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (writeHeader)
        {
            await WriteHeaderAsync<T>(cancellationToken).ConfigureAwait(false);
        }

        await foreach (var record in records.ConfigureAwait(false).WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteRecordAsync(record!, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Flush()
    {
        _output.Flush();
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        return _output.FlushAsync(cancellationToken);
    }

    public void Dispose()
    {
        _output.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        return _output.DisposeAsync();
    }

    private CsvValueConversionPlan[] ResolveMemberFormattingPlans(CsvTypeMap map)
    {
        if (_memberFormattingPlanCache.TryGetValue(map.RecordType, out var cached))
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

        _memberFormattingPlanCache[map.RecordType] = plans;
        return plans;
    }

    private int[] ResolveWritableMemberIndices(CsvTypeMap map)
    {
        if (_writableMemberIndexCache.TryGetValue(map.RecordType, out var cached))
        {
            return cached;
        }

        var members = new List<int>(map.Members.Length);
        for (var i = 0; i < map.Members.Length; i++)
        {
            var member = map.Members[i];
            if (member.Ignore || member.Getter is null)
            {
                continue;
            }

            members.Add(i);
        }

        var indices = members.ToArray();
        _writableMemberIndexCache[map.RecordType] = indices;
        return indices;
    }

    private CsvSimpleWritePlan? ResolveSimpleWritePlan(
        CsvTypeMap map,
        int[] writableMemberIndices,
        CsvValueConversionPlan[] formattingPlans)
    {
        if (_simpleWritePlanCache.TryGetValue(map.RecordType, out var cached))
        {
            return cached;
        }

        var members = new CsvSimpleWriteMember[writableMemberIndices.Length];
        for (var i = 0; i < writableMemberIndices.Length; i++)
        {
            var memberIndex = writableMemberIndices[i];
            var member = map.Members[memberIndex];
            var formattingPlan = formattingPlans[memberIndex];
            var writeMethod = ResolveBuiltInWriterMethod(member.PropertyType, formattingPlan);

            if (member.HasConstant ||
                member.HasDefault ||
                member.Validation is not null ||
                formattingPlan.Converter is not null ||
                writeMethod is null)
            {
                _simpleWritePlanCache[map.RecordType] = null;
                return null;
            }

            members[i] = new CsvSimpleWriteMember(member.Property, writeMethod, formattingPlan.ConverterOptions);
        }

        var plan = new CsvSimpleWritePlan(members);
        _simpleWritePlanCache[map.RecordType] = plan;
        return plan;
    }

    private Action<CsvWriter, T>? ResolveCompiledSimpleWriter<T>(CsvSimpleWritePlan plan)
    {
        if (_compiledSimpleWriterCache.TryGetValue(typeof(T), out var cached))
        {
            return (Action<CsvWriter, T>)cached;
        }

        var compiled = BuildCompiledSimpleWriter<T>(plan);
        _compiledSimpleWriterCache[typeof(T)] = compiled;
        return compiled;
    }

    private Func<CsvWriter, T, CancellationToken, ValueTask>? ResolveCompiledAsyncSimpleWriter<T>(CsvSimpleWritePlan plan)
    {
        if (_compiledAsyncSimpleWriterCache.TryGetValue(typeof(T), out var cached))
        {
            return (Func<CsvWriter, T, CancellationToken, ValueTask>)cached;
        }

        var compiled = BuildCompiledAsyncSimpleWriter<T>(plan);
        _compiledAsyncSimpleWriterCache[typeof(T)] = compiled;
        return compiled;
    }

    private static Action<CsvWriter, T> BuildCompiledSimpleWriter<T>(CsvSimpleWritePlan plan)
    {
        var writer = Expression.Parameter(typeof(CsvWriter), "writer");
        var record = Expression.Parameter(typeof(T), "record");
        var expressions = new List<Expression>(plan.Members.Length);

        for (var i = 0; i < plan.Members.Length; i++)
        {
            var member = plan.Members[i];
            Expression recordAccess = record;
            if (member.Property.DeclaringType is not null && member.Property.DeclaringType != typeof(T))
            {
                recordAccess = Expression.Convert(recordAccess, member.Property.DeclaringType);
            }

            var propertyAccess = Expression.Property(recordAccess, member.Property);
            if (member.ConverterOptions is null)
            {
                expressions.Add(Expression.Call(writer, member.WriteMethod, propertyAccess));
            }
            else
            {
                expressions.Add(Expression.Call(
                    writer,
                    member.WriteMethod,
                    propertyAccess,
                    Expression.Constant(member.ConverterOptions, typeof(CsvTypeConverterOptions))));
            }
        }

        var body = Expression.Block(expressions);
        return Expression.Lambda<Action<CsvWriter, T>>(body, writer, record).Compile();
    }

    private static Func<CsvWriter, T, CancellationToken, ValueTask> BuildCompiledAsyncSimpleWriter<T>(
        CsvSimpleWritePlan plan)
    {
        var accessors = new CsvAsyncSimpleWriteAccessor<T>[plan.Members.Length];
        for (var i = 0; i < plan.Members.Length; i++)
        {
            accessors[i] = CreateAsyncSimpleWriteAccessor<T>(plan.Members[i]);
        }

        return async (writer, record, cancellationToken) =>
        {
            for (var i = 0; i < accessors.Length; i++)
            {
                await accessors[i].WriteAsync(writer, record, cancellationToken).ConfigureAwait(false);
            }
        };
    }

    private static CsvAsyncSimpleWriteAccessor<T> CreateAsyncSimpleWriteAccessor<T>(in CsvSimpleWriteMember member)
    {
        var factory = typeof(CsvWriter).GetMethod(nameof(CreateAsyncSimpleWriteAccessorCore),
                BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(T), member.Property.PropertyType);
        return (CsvAsyncSimpleWriteAccessor<T>)factory.Invoke(null, [member])!;
    }

    private static CsvAsyncSimpleWriteAccessor<TRecord> CreateAsyncSimpleWriteAccessorCore<TRecord, TProperty>(
        CsvSimpleWriteMember member)
    {
        var getter = BuildTypedPropertyGetter<TRecord, TProperty>(member.Property);
        var asyncWriteMethod = ResolveBuiltInAsyncWriterMethod(typeof(TProperty))
            ?? throw new InvalidOperationException($"No async writer method for '{typeof(TProperty).Name}'.");
        var writerDelegate =
            (Func<CsvWriter, TProperty, CsvTypeConverterOptions?, CancellationToken, ValueTask>)asyncWriteMethod
            .CreateDelegate(typeof(Func<CsvWriter, TProperty, CsvTypeConverterOptions?, CancellationToken, ValueTask>));
        return new CsvAsyncSimpleWriteAccessor<TRecord, TProperty>(getter, writerDelegate, member.ConverterOptions);
    }

    private static Func<TRecord, TProperty> BuildTypedPropertyGetter<TRecord, TProperty>(PropertyInfo property)
    {
        var record = Expression.Parameter(typeof(TRecord), "record");
        Expression recordAccess = record;
        if (property.DeclaringType is not null && property.DeclaringType != typeof(TRecord))
        {
            recordAccess = Expression.Convert(recordAccess, property.DeclaringType);
        }

        var body = Expression.Property(recordAccess, property);
        return Expression.Lambda<Func<TRecord, TProperty>>(body, record).Compile();
    }

    private static MethodInfo? ResolveBuiltInWriterMethod(Type propertyType, in CsvValueConversionPlan formattingPlan)
    {
        var hasConverterOptions = formattingPlan.ConverterOptions is not null;

        if (propertyType == typeof(string))
        {
            return hasConverterOptions ? WriteStringFieldValueWithOptionsMethod : WriteStringFieldValueMethod;
        }

        if (propertyType == typeof(int))
        {
            return hasConverterOptions ? WriteInt32FieldValueWithOptionsMethod : WriteInt32FieldValueMethod;
        }

        if (propertyType == typeof(int?))
        {
            return hasConverterOptions ? WriteNullableInt32FieldValueWithOptionsMethod : WriteNullableInt32FieldValueMethod;
        }

        if (propertyType == typeof(decimal))
        {
            return hasConverterOptions ? WriteDecimalFieldValueWithOptionsMethod : WriteDecimalFieldValueMethod;
        }

        if (propertyType == typeof(decimal?))
        {
            return hasConverterOptions ? WriteNullableDecimalFieldValueWithOptionsMethod : WriteNullableDecimalFieldValueMethod;
        }

        if (propertyType == typeof(DateTime))
        {
            return hasConverterOptions ? WriteDateTimeFieldValueWithOptionsMethod : WriteDateTimeFieldValueMethod;
        }

        if (propertyType == typeof(DateTime?))
        {
            return hasConverterOptions ? WriteNullableDateTimeFieldValueWithOptionsMethod : WriteNullableDateTimeFieldValueMethod;
        }

        if (propertyType == typeof(bool))
        {
            return hasConverterOptions ? WriteBooleanFieldValueWithOptionsMethod : WriteBooleanFieldValueMethod;
        }

        if (propertyType == typeof(bool?))
        {
            return hasConverterOptions ? WriteNullableBooleanFieldValueWithOptionsMethod : WriteNullableBooleanFieldValueMethod;
        }

        return null;
    }

    private static MethodInfo? ResolveBuiltInAsyncWriterMethod(Type propertyType)
    {
        if (propertyType == typeof(string))
        {
            return typeof(CsvWriter).GetMethod(nameof(WriteStringFieldValueAsync), BindingFlags.Instance | BindingFlags.NonPublic);
        }

        if (propertyType == typeof(int))
        {
            return typeof(CsvWriter).GetMethod(nameof(WriteInt32FieldValueAsync), BindingFlags.Instance | BindingFlags.NonPublic);
        }

        if (propertyType == typeof(int?))
        {
            return typeof(CsvWriter).GetMethod(nameof(WriteNullableInt32FieldValueAsync), BindingFlags.Instance | BindingFlags.NonPublic);
        }

        if (propertyType == typeof(decimal))
        {
            return typeof(CsvWriter).GetMethod(nameof(WriteDecimalFieldValueAsync), BindingFlags.Instance | BindingFlags.NonPublic);
        }

        if (propertyType == typeof(decimal?))
        {
            return typeof(CsvWriter).GetMethod(nameof(WriteNullableDecimalFieldValueAsync), BindingFlags.Instance | BindingFlags.NonPublic);
        }

        if (propertyType == typeof(DateTime))
        {
            return typeof(CsvWriter).GetMethod(nameof(WriteDateTimeFieldValueAsync), BindingFlags.Instance | BindingFlags.NonPublic);
        }

        if (propertyType == typeof(DateTime?))
        {
            return typeof(CsvWriter).GetMethod(nameof(WriteNullableDateTimeFieldValueAsync), BindingFlags.Instance | BindingFlags.NonPublic);
        }

        if (propertyType == typeof(bool))
        {
            return typeof(CsvWriter).GetMethod(nameof(WriteBooleanFieldValueAsync), BindingFlags.Instance | BindingFlags.NonPublic);
        }

        if (propertyType == typeof(bool?))
        {
            return typeof(CsvWriter).GetMethod(nameof(WriteNullableBooleanFieldValueAsync), BindingFlags.Instance | BindingFlags.NonPublic);
        }

        return null;
    }

    private void WriteDelimiterIfNeeded()
    {
        if (_firstField)
        {
            return;
        }

        if (_utf8Output is not null)
        {
            _utf8Output.WriteUtf8(_delimiterUtf8!);
            return;
        }

        var delimiter = Options.DelimiterString;
        if (delimiter.Length == 1)
        {
            _charScratch[0] = delimiter[0];
            _output.Write(_charScratch.AsSpan(0, 1));
            return;
        }

        _output.Write(delimiter.AsSpan());
    }

    private ValueTask WriteDelimiterIfNeededAsync(CancellationToken cancellationToken)
    {
        if (_firstField)
        {
            return default;
        }

        if (_utf8Output is not null)
        {
            return _utf8Output.WriteUtf8Async(_delimiterUtf8!, cancellationToken);
        }

        var delimiter = Options.DelimiterString;
        if (delimiter.Length == 1)
        {
            _charScratch[0] = delimiter[0];
            return _output.WriteAsync(_charScratch.AsMemory(0, 1), cancellationToken);
        }

        return _output.WriteAsync(delimiter.AsMemory(), cancellationToken);
    }

    private static bool ContainsDelimiter(ReadOnlySpan<char> source, ReadOnlySpan<char> delimiter)
    {
        return delimiter.Length != 0 &&
               source.Length >= delimiter.Length &&
               source.IndexOf(delimiter) >= 0;
    }

    private bool NeedsQuoting(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return false;
        }

        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
        {
            return true;
        }

        if (ContainsDelimiter(value, Options.DelimiterString.AsSpan()))
        {
            return true;
        }

        foreach (var ch in value)
        {
            if (ch == Options.Quote || ch == '\r' || ch == '\n')
            {
                return true;
            }
        }

        return false;
    }

    private void WriteEscaped(ReadOnlySpan<char> value)
    {
        var segmentStart = 0;

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != Options.Quote)
            {
                continue;
            }

            if (i > segmentStart)
            {
                _output.Write(value[segmentStart..i]);
            }

            _charScratch[0] = Options.Escape;
            _charScratch[1] = Options.Quote;
            _output.Write(_charScratch.AsSpan(0, 2));
            segmentStart = i + 1;
        }

        if (segmentStart < value.Length)
        {
            _output.Write(value[segmentStart..]);
        }
    }

    private async ValueTask WriteEscapedAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken)
    {
        var segmentStart = 0;
        var length = value.Length;

        for (var i = 0; i < length; i++)
        {
            if (value.Span[i] != Options.Quote)
            {
                continue;
            }

            if (i > segmentStart)
            {
                await _output.WriteAsync(value[segmentStart..i], cancellationToken).ConfigureAwait(false);
            }

            _charScratch[0] = Options.Escape;
            _charScratch[1] = Options.Quote;
            await _output.WriteAsync(_charScratch.AsMemory(0, 2), cancellationToken).ConfigureAwait(false);
            segmentStart = i + 1;
        }

        if (segmentStart < length)
        {
            await _output.WriteAsync(value[segmentStart..], cancellationToken).ConfigureAwait(false);
        }
    }

    private void WriteChar(char value)
    {
        if (_utf8Output is not null && value <= 0x7F)
        {
            _utf8Output.WriteByte((byte)value);
            return;
        }

        _charScratch[0] = value;
        _output.Write(_charScratch.AsSpan(0, 1));
    }

    private ValueTask WriteCharAsync(char value, CancellationToken cancellationToken)
    {
        if (_utf8Output is not null && value <= 0x7F)
        {
            return _utf8Output.WriteByteAsync((byte)value, cancellationToken);
        }

        _charScratch[0] = value;
        return _output.WriteAsync(_charScratch.AsMemory(0, 1), cancellationToken);
    }

    private async ValueTask WriteFieldCoreAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken)
    {
        if (TrySanitizeForInjection(value.Span, out var sanitized))
        {
            value = sanitized.AsMemory();
        }

        await WriteDelimiterIfNeededAsync(cancellationToken).ConfigureAwait(false);

        if (NeedsQuoting(value.Span))
        {
            await WriteCharAsync(Options.Quote, cancellationToken).ConfigureAwait(false);
            await WriteEscapedAsync(value, cancellationToken).ConfigureAwait(false);
            await WriteCharAsync(Options.Quote, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _output.WriteAsync(value, cancellationToken).ConfigureAwait(false);
        }

        _firstField = false;
        _fieldIndex++;
    }

    private bool TrySanitizeForInjection(ReadOnlySpan<char> value, out string sanitized)
    {
        sanitized = string.Empty;

        if (!Options.SanitizeForInjection || value.IsEmpty)
        {
            return false;
        }

        if (Options.InjectionCharacters.AsSpan().IndexOf(value[0]) < 0)
        {
            return false;
        }

        sanitized = string.Concat(Options.InjectionEscapeCharacter, value.ToString());
        return true;
    }

    private void WriteFormattedInt32(int value, CsvTypeConverterOptions? converterOptions)
    {
        var culture = converterOptions?.CultureInfo ?? Options.CultureInfo;
        var format = converterOptions is { Formats.Count: > 0 } ? converterOptions.Formats[0] : null;
        if (TryWriteUtf8FormattedInt32(value, format, culture))
        {
            return;
        }

        Span<char> buffer = stackalloc char[32];
        if (value.TryFormat(buffer, out var written, format, culture))
        {
            WriteField(buffer[..written]);
            return;
        }

        WriteField(value.ToString(format, culture).AsSpan());
    }

    private void WriteFormattedDecimal(decimal value, CsvTypeConverterOptions? converterOptions)
    {
        var culture = converterOptions?.CultureInfo ?? Options.CultureInfo;
        var format = converterOptions is { Formats.Count: > 0 } ? converterOptions.Formats[0] : null;
        if (TryWriteUtf8FormattedDecimal(value, format, culture))
        {
            return;
        }

        Span<char> buffer = stackalloc char[64];
        if (value.TryFormat(buffer, out var written, format, culture))
        {
            WriteField(buffer[..written]);
            return;
        }

        WriteField(value.ToString(format, culture).AsSpan());
    }

    private void WriteFormattedDateTime(DateTime value, CsvTypeConverterOptions? converterOptions)
    {
        var culture = converterOptions?.CultureInfo ?? Options.CultureInfo;
        var format = converterOptions is { Formats.Count: > 0 } ? converterOptions.Formats[0] : null;
        if (TryWriteUtf8FormattedDateTime(value, format, culture))
        {
            return;
        }

        Span<char> buffer = stackalloc char[128];
        if (value.TryFormat(buffer, out var written, format, culture))
        {
            WriteField(buffer[..written]);
            return;
        }

        WriteField(value.ToString(format, culture).AsSpan());
    }

    private void WriteNullTokenOrEmpty(CsvTypeConverterOptions? converterOptions)
    {
        if (converterOptions is { NullValues.Count: > 0 })
        {
            if (TryWriteUtf8AsciiField(converterOptions.NullValues[0]))
            {
                return;
            }

            WriteField(converterOptions.NullValues[0].AsSpan());
            return;
        }

        if (TryWriteUtf8Field(ReadOnlySpan<byte>.Empty))
        {
            return;
        }

        WriteField(ReadOnlySpan<char>.Empty);
    }

    private bool TryWriteUtf8FormattedInt32(int value, string? format, CultureInfo culture)
    {
        if (_utf8Output is null || format is not null || !IsInvariantCulture(culture))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[32];
        return Utf8Formatter.TryFormat(value, buffer, out var written) && TryWriteUtf8Field(buffer[..written]);
    }

    private bool TryWriteUtf8FormattedDecimal(decimal value, string? format, CultureInfo culture)
    {
        if (_utf8Output is null || format is not null || !IsInvariantCulture(culture))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[64];
        return Utf8Formatter.TryFormat(value, buffer, out var written) && TryWriteUtf8Field(buffer[..written]);
    }

    private bool TryWriteUtf8FormattedDateTime(DateTime value, string? format, CultureInfo culture)
    {
        if (_utf8Output is null || !IsInvariantCulture(culture))
        {
            return false;
        }

        StandardFormat standardFormat;
        if (string.IsNullOrEmpty(format))
        {
            standardFormat = new StandardFormat('G');
        }
        else if (format.Length == 1 && TryGetSupportedUtf8DateTimeFormat(format[0], out standardFormat))
        {
        }
        else
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[64];
        return Utf8Formatter.TryFormat(value, buffer, out var written, standardFormat) &&
               TryWriteUtf8Field(buffer[..written]);
    }

    private bool TryWriteUtf8AsciiField(string value)
    {
        if (_utf8Output is null || !IsAscii(value))
        {
            return false;
        }

        if (value.Length <= 256)
        {
            Span<byte> buffer = stackalloc byte[value.Length];
            for (var i = 0; i < value.Length; i++)
            {
                buffer[i] = (byte)value[i];
            }

            return TryWriteUtf8Field(buffer);
        }

        var bufferArray = new byte[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            bufferArray[i] = (byte)value[i];
        }

        return TryWriteUtf8Field(bufferArray);
    }

    private bool TryWriteUtf8Field(ReadOnlySpan<byte> value)
    {
        if (_utf8Output is null || !CanWriteUtf8FieldDirect(value))
        {
            return false;
        }

        WriteDelimiterIfNeeded();
        _utf8Output.WriteUtf8(value);
        _firstField = false;
        _fieldIndex++;
        return true;
    }

    private bool CanWriteUtf8FieldDirect(ReadOnlySpan<byte> value)
    {
        if (RequiresInjectionPrefix(value))
        {
            return false;
        }

        if (value.IsEmpty)
        {
            return true;
        }

        if (IsAsciiWhitespace(value[0]) || IsAsciiWhitespace(value[^1]))
        {
            return false;
        }

        if (_delimiterUtf8 is { Length: > 0 } delimiterUtf8 &&
            value.Length >= delimiterUtf8.Length &&
            value.IndexOf(delimiterUtf8) >= 0)
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var b = value[i];
            if ((_utf8Output is not null && Options.Quote <= 0x7F && b == (byte)Options.Quote) ||
                b == (byte)'\r' ||
                b == (byte)'\n')
            {
                return false;
            }
        }

        return true;
    }

    private bool RequiresInjectionPrefix(ReadOnlySpan<byte> value)
    {
        if (!Options.SanitizeForInjection || value.IsEmpty)
        {
            return false;
        }

        var first = value[0];
        foreach (var ch in Options.InjectionCharacters)
        {
            if (ch <= 0x7F && first == (byte)ch)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInvariantCulture(CultureInfo culture)
    {
        return culture.Name.Length == 0;
    }

    private static bool IsAscii(string value)
    {
        foreach (var ch in value)
        {
            if (ch > 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiWhitespace(byte value)
    {
        return value == (byte)' ' || value == (byte)'\t' || value == (byte)'\f' || value == (byte)'\v';
    }

    private static bool TryGetSupportedUtf8DateTimeFormat(char format, out StandardFormat standardFormat)
    {
        switch (format)
        {
            case 'G':
            case 'O':
            case 'R':
            case 'l':
                standardFormat = new StandardFormat(format);
                return true;
            default:
                standardFormat = default;
                return false;
        }
    }

    private ValueTask WriteFormattedInt32Async(int value, CsvTypeConverterOptions? converterOptions,
        CancellationToken cancellationToken)
    {
        var culture = converterOptions?.CultureInfo ?? Options.CultureInfo;
        var format = converterOptions is { Formats.Count: > 0 } ? converterOptions.Formats[0] : null;
        char[]? rented = null;
        try
        {
            rented = new char[32];
            if (value.TryFormat(rented, out var written, format, culture))
            {
                return WriteFieldCoreAsync(rented.AsMemory(0, written), cancellationToken);
            }
        }
        finally
        {
            if (rented is not null)
            {
                // no-op, array was temporary and not pooled
            }
        }

        return WriteFieldCoreAsync(value.ToString(format, culture).AsMemory(), cancellationToken);
    }

    private ValueTask WriteFormattedDecimalAsync(decimal value, CsvTypeConverterOptions? converterOptions,
        CancellationToken cancellationToken)
    {
        var culture = converterOptions?.CultureInfo ?? Options.CultureInfo;
        var format = converterOptions is { Formats.Count: > 0 } ? converterOptions.Formats[0] : null;
        var buffer = new char[64];
        if (value.TryFormat(buffer, out var written, format, culture))
        {
            return WriteFieldCoreAsync(buffer.AsMemory(0, written), cancellationToken);
        }

        return WriteFieldCoreAsync(value.ToString(format, culture).AsMemory(), cancellationToken);
    }

    private ValueTask WriteFormattedDateTimeAsync(DateTime value, CsvTypeConverterOptions? converterOptions,
        CancellationToken cancellationToken)
    {
        var culture = converterOptions?.CultureInfo ?? Options.CultureInfo;
        var format = converterOptions is { Formats.Count: > 0 } ? converterOptions.Formats[0] : null;
        var buffer = new char[128];
        if (value.TryFormat(buffer, out var written, format, culture))
        {
            return WriteFieldCoreAsync(buffer.AsMemory(0, written), cancellationToken);
        }

        return WriteFieldCoreAsync(value.ToString(format, culture).AsMemory(), cancellationToken);
    }

    private ValueTask WriteNullTokenOrEmptyAsync(CsvTypeConverterOptions? converterOptions,
        CancellationToken cancellationToken)
    {
        if (converterOptions is { NullValues.Count: > 0 })
        {
            return WriteFieldCoreAsync(converterOptions.NullValues[0].AsMemory(), cancellationToken);
        }

        return WriteFieldCoreAsync(ReadOnlyMemory<char>.Empty, cancellationToken);
    }

    private readonly record struct CsvSimpleWriteMember(
        PropertyInfo Property,
        MethodInfo WriteMethod,
        CsvTypeConverterOptions? ConverterOptions);

    private sealed class CsvSimpleWritePlan(CsvSimpleWriteMember[] members)
    {
        public CsvSimpleWriteMember[] Members { get; } = members;
    }

    private abstract class CsvAsyncSimpleWriteAccessor<TRecord>
    {
        public abstract ValueTask WriteAsync(CsvWriter writer, TRecord record, CancellationToken cancellationToken);
    }

    private sealed class CsvAsyncSimpleWriteAccessor<TRecord, TProperty>(
        Func<TRecord, TProperty> getter,
        Func<CsvWriter, TProperty, CsvTypeConverterOptions?, CancellationToken, ValueTask> writerDelegate,
        CsvTypeConverterOptions? converterOptions)
        : CsvAsyncSimpleWriteAccessor<TRecord>
    {
        public override ValueTask WriteAsync(CsvWriter writer, TRecord record, CancellationToken cancellationToken)
        {
            return writerDelegate(writer, getter(record), converterOptions, cancellationToken);
        }
    }
}
