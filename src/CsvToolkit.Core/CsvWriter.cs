using System.Buffers;
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
        ArgumentNullException.ThrowIfNull(writer);

        Options = (options ?? CsvOptions.Default).Clone();
        Options.Validate();
        _mapRegistry = mapRegistry ?? new CsvMapRegistry();
        _output = new TextWriterOutput(writer, leaveOpen);
    }

    public CsvWriter(Stream stream, CsvOptions? options = null, CsvMapRegistry? mapRegistry = null,
        bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

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

    public ValueTask WriteHeaderAsync<T>(CancellationToken cancellationToken = default)
    {
        WriteHeader<T>();
        return ValueTask.CompletedTask;
    }

    private void WriteField(ReadOnlySpan<char> value)
    {
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
        WriteField(value.Span);
        return ValueTask.CompletedTask;
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

        if (value is ISpanFormattable formattable)
        {
            // Fast path: format directly into stack/pooled buffers to avoid intermediate strings.
            Span<char> stack = stackalloc char[128];
            if (formattable.TryFormat(stack, out var written, default, Options.CultureInfo))
            {
                WriteField(stack[..written]);
                return;
            }

            var pooled = ArrayPool<char>.Shared.Rent(1024);
            try
            {
                var success = false;
                while (!success)
                {
                    success = formattable.TryFormat(pooled, out written, default, Options.CultureInfo);
                    if (!success)
                    {
                        ArrayPool<char>.Shared.Return(pooled);
                        pooled = ArrayPool<char>.Shared.Rent(pooled.Length * 2);
                    }
                }

                WriteField(pooled.AsSpan(0, written));
                return;
            }
            finally
            {
                ArrayPool<char>.Shared.Return(pooled);
            }
        }

        var context = new CsvConverterContext(Options.CultureInfo, RowIndex, _fieldIndex, null);
        var formatted = CsvValueConverter.FormatToString(value, value.GetType(), Options, null, context);
        WriteField(formatted.AsSpan());
    }

    public ValueTask WriteFieldAsync<T>(T value, CancellationToken cancellationToken = default)
    {
        WriteField(value);
        return ValueTask.CompletedTask;
    }

    public void NextRecord()
    {
        var newLine = Options.NewLine ?? Environment.NewLine;
        _output.Write(newLine.AsSpan());
        _firstField = true;
        _fieldIndex = 0;
        RowIndex++;
    }

    public ValueTask NextRecordAsync(CancellationToken cancellationToken = default)
    {
        NextRecord();
        return ValueTask.CompletedTask;
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

            var value = member.Getter(boxed);
            if (member.Converter is not null)
            {
                var context = new CsvConverterContext(Options.CultureInfo, RowIndex, _fieldIndex, member.Name);
                var formatted =
                    CsvValueConverter.FormatToString(value, member.PropertyType, Options, member.Converter, context);
                WriteField(formatted.AsSpan());
            }
            else if (value is null)
            {
                WriteField(ReadOnlySpan<char>.Empty);
            }
            else
            {
                WriteField(value);
            }
        }

        NextRecord();
    }

    public ValueTask WriteRecordAsync<T>(T record, CancellationToken cancellationToken = default)
    {
        WriteRecord(record);
        return ValueTask.CompletedTask;
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

        _charScratch[0] = Options.Delimiter;
        _output.Write(_charScratch.AsSpan(0, 1));
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

        foreach (var ch in value)
        {
            if (ch == Options.Delimiter || ch == Options.Quote || ch == '\r' || ch == '\n')
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

    private void WriteChar(char value)
    {
        _charScratch[0] = value;
        _output.Write(_charScratch.AsSpan(0, 1));
    }
}