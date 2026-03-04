using CsvToolkit.Core.Internal;
using CsvToolkit.Core.Mapping;
using CsvToolkit.Core.TypeConversion;

namespace CsvToolkit.Core;

/// <summary>
/// Streaming CSV writer with low-allocation field writing and POCO mapping support.
/// </summary>
public sealed class CsvWriter : IDisposable, IAsyncDisposable
{
    private readonly ICsvCharOutput _output;
    private readonly CsvMapRegistry _mapRegistry;
    private readonly char[] _charScratch = new char[2];
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
        _output = new Utf8StreamOutput(stream, Options.ByteBufferSize, leaveOpen);
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
        var newLine = Options.NewLine ?? Environment.NewLine;
        _output.Write(newLine.AsSpan());
        _firstField = true;
        _fieldIndex = 0;
        RowIndex++;
    }

    public async ValueTask NextRecordAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var newLine = Options.NewLine ?? Environment.NewLine;
        await _output.WriteAsync(newLine.AsMemory(), cancellationToken).ConfigureAwait(false);
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
        object boxed = record;

        foreach (var member in map.Members)
        {
            if (member.Ignore || member.Getter is null)
            {
                continue;
            }

            var value = member.HasConstant ? member.ConstantValue : member.Getter(boxed);
            if (value is null && member.HasDefault)
            {
                value = member.DefaultValue;
            }

            if (member.Validation is not null && !member.Validation(value))
            {
                var message = member.ValidationMessage ?? $"Validation failed for member '{member.Name}'.";
                throw new InvalidOperationException(message);
            }

            var context = new CsvConverterContext(Options.CultureInfo, RowIndex, _fieldIndex, member.Name);
            var formatted =
                CsvValueConverter.FormatToString(value, member.PropertyType, Options, member.Converter,
                    member.ConverterOptions, context);
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
        object boxed = record;

        foreach (var member in map.Members)
        {
            if (member.Ignore || member.Getter is null)
            {
                continue;
            }

            var value = member.HasConstant ? member.ConstantValue : member.Getter(boxed);
            if (value is null && member.HasDefault)
            {
                value = member.DefaultValue;
            }

            if (member.Validation is not null && !member.Validation(value))
            {
                var message = member.ValidationMessage ?? $"Validation failed for member '{member.Name}'.";
                throw new InvalidOperationException(message);
            }

            var context = new CsvConverterContext(Options.CultureInfo, RowIndex, _fieldIndex, member.Name);
            var formatted =
                CsvValueConverter.FormatToString(value, member.PropertyType, Options, member.Converter,
                    member.ConverterOptions, context);
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

        await foreach (var record in records.ConfigureAwait(false))
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

    private void WriteDelimiterIfNeeded()
    {
        if (_firstField)
        {
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

        var delimiter = Options.DelimiterString;
        if (delimiter.Length == 1)
        {
            _charScratch[0] = delimiter[0];
            return _output.WriteAsync(_charScratch.AsMemory(0, 1), cancellationToken);
        }

        return _output.WriteAsync(delimiter.AsMemory(), cancellationToken);
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

    private static bool ContainsDelimiter(ReadOnlySpan<char> source, ReadOnlySpan<char> delimiter)
    {
        if (delimiter.Length == 0 || source.Length < delimiter.Length)
        {
            return false;
        }

        for (var i = 0; i <= source.Length - delimiter.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < delimiter.Length; j++)
            {
                if (source[i + j] == delimiter[j])
                {
                    continue;
                }

                matched = false;
                break;
            }

            if (matched)
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
        _charScratch[0] = value;
        _output.Write(_charScratch.AsSpan(0, 1));
    }

    private ValueTask WriteCharAsync(char value, CancellationToken cancellationToken)
    {
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
}
