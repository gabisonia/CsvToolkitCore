using System.Buffers;
using System.Runtime.CompilerServices;

namespace CsvToolkit.Core.Internal;

internal sealed class CsvParser(ICsvCharInput input, CsvOptions options) : IDisposable, IAsyncDisposable
{
    private readonly CsvRowBuffer _rowBuffer = new(options.CharBufferSize);
    private readonly char[] _readBuffer = ArrayPool<char>.Shared.Rent(options.CharBufferSize);
    private int _readPosition;
    private int _readLength;
    private int _pushback = -1;

    public CsvRow CurrentRow { get; private set; }

    private long RowIndex { get; set; }

    private long LineNumber { get; set; } = 1;

    public string? DetectedNewLine { get; private set; }

    public bool TryReadRow(out CsvRow row)
    {
        var read = TryReadRowCore();
        row = CurrentRow;
        return read;
    }

    public ValueTask<bool> TryReadRowAsync(CancellationToken cancellationToken)
    {
        return TryReadRowCoreAsync(cancellationToken);
    }

    public void Dispose()
    {
        _rowBuffer.Dispose();
        ArrayPool<char>.Shared.Return(_readBuffer);
        input.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _rowBuffer.Dispose();
        ArrayPool<char>.Shared.Return(_readBuffer);
        await input.DisposeAsync().ConfigureAwait(false);
    }

    private bool TryReadRowCore()
    {
        _rowBuffer.Reset();

        var delimiter = options.Delimiter;
        var quote = options.Quote;
        var escape = options.Escape;
        var trimOptions = options.TrimOptions;
        var ignoreBlankLines = options.IgnoreBlankLines;
        var trimStartEnabled = (trimOptions & CsvTrimOptions.TrimStart) != 0;
        var hasDistinctEscape = escape != quote;

        var inQuotes = false;
        var afterClosingQuote = false;
        var fieldWasQuoted = false;
        var consumedAnything = false;

        while (true)
        {
            var code = ReadChar();

            if (code < 0)
            {
                if (!consumedAnything && _rowBuffer.FieldCount == 0 && _rowBuffer.CurrentFieldLength == 0)
                {
                    return false;
                }

                if (inQuotes)
                {
                    HandleBadData(
                        _rowBuffer.FieldCount,
                        "Unexpected end of file while inside a quoted field.",
                        _rowBuffer.CurrentFieldMemory);
                }

                _rowBuffer.CompleteField(fieldWasQuoted, trimOptions);
                if (ignoreBlankLines && _rowBuffer.IsBlankLine())
                {
                    return false;
                }

                CurrentRow = _rowBuffer.ToRow(RowIndex, LineNumber);
                RowIndex++;
                return true;
            }

            consumedAnything = true;
            var ch = (char)code;

            if (inQuotes)
            {
                if (hasDistinctEscape && ch == escape)
                {
                    var escaped = ReadChar();

                    if (escaped == quote)
                    {
                        _rowBuffer.Append(quote);
                        continue;
                    }

                    if (escaped >= 0)
                    {
                        PushBack(escaped);
                    }

                    _rowBuffer.Append(ch);
                    continue;
                }

                if (ch == quote)
                {
                    var next = ReadChar();

                    if (next == quote)
                    {
                        _rowBuffer.Append(quote);
                        continue;
                    }

                    inQuotes = false;
                    afterClosingQuote = true;

                    if (next >= 0)
                    {
                        PushBack(next);
                    }

                    continue;
                }

                _rowBuffer.Append(ch);
                continue;
            }

            if (afterClosingQuote)
            {
                if (ch == delimiter)
                {
                    _rowBuffer.CompleteField(true, trimOptions);
                    fieldWasQuoted = false;
                    afterClosingQuote = false;
                    continue;
                }

                if (ch == '\r' || ch == '\n')
                {
                    ConsumeNewLineSuffix(ch);
                    _rowBuffer.CompleteField(true, trimOptions);
                    if (ignoreBlankLines && _rowBuffer.IsBlankLine())
                    {
                        ResetRowState(ref consumedAnything, ref inQuotes, ref afterClosingQuote, ref fieldWasQuoted);
                        continue;
                    }

                    CurrentRow = _rowBuffer.ToRow(RowIndex, LineNumber);
                    RowIndex++;
                    return true;
                }

                if (char.IsWhiteSpace(ch))
                {
                    continue;
                }

                HandleBadData(
                    _rowBuffer.FieldCount,
                    "Unexpected character after closing quote.",
                    _rowBuffer.CurrentFieldMemory);

                afterClosingQuote = false;
                _rowBuffer.Append(ch);
                continue;
            }

            if (ch == delimiter)
            {
                _rowBuffer.CompleteField(fieldWasQuoted, trimOptions);
                fieldWasQuoted = false;
                continue;
            }

            if (ch == quote)
            {
                if (_rowBuffer.CurrentFieldLength == 0)
                {
                    inQuotes = true;
                    fieldWasQuoted = true;
                    continue;
                }

                HandleBadData(
                    _rowBuffer.FieldCount,
                    "Unexpected quote in unquoted field.",
                    _rowBuffer.CurrentFieldMemory);

                _rowBuffer.Append(ch);
                continue;
            }

            if (ch == '\r' || ch == '\n')
            {
                ConsumeNewLineSuffix(ch);
                _rowBuffer.CompleteField(fieldWasQuoted, trimOptions);
                if (ignoreBlankLines && _rowBuffer.IsBlankLine())
                {
                    ResetRowState(ref consumedAnything, ref inQuotes, ref afterClosingQuote, ref fieldWasQuoted);
                    continue;
                }

                CurrentRow = _rowBuffer.ToRow(RowIndex, LineNumber);
                RowIndex++;
                return true;
            }

            if (_rowBuffer.CurrentFieldLength == 0 && trimStartEnabled && char.IsWhiteSpace(ch))
            {
                continue;
            }

            _rowBuffer.Append(ch);
        }
    }

    private async ValueTask<bool> TryReadRowCoreAsync(CancellationToken cancellationToken)
    {
        _rowBuffer.Reset();

        var delimiter = options.Delimiter;
        var quote = options.Quote;
        var escape = options.Escape;
        var trimOptions = options.TrimOptions;
        var ignoreBlankLines = options.IgnoreBlankLines;
        var trimStartEnabled = (trimOptions & CsvTrimOptions.TrimStart) != 0;
        var hasDistinctEscape = escape != quote;

        var inQuotes = false;
        var afterClosingQuote = false;
        var fieldWasQuoted = false;
        var consumedAnything = false;

        while (true)
        {
            var code = await ReadCharAsync(cancellationToken).ConfigureAwait(false);

            if (code < 0)
            {
                if (!consumedAnything && _rowBuffer.FieldCount == 0 && _rowBuffer.CurrentFieldLength == 0)
                {
                    return false;
                }

                if (inQuotes)
                {
                    HandleBadData(
                        _rowBuffer.FieldCount,
                        "Unexpected end of file while inside a quoted field.",
                        _rowBuffer.CurrentFieldMemory);
                }

                _rowBuffer.CompleteField(fieldWasQuoted, trimOptions);
                if (ignoreBlankLines && _rowBuffer.IsBlankLine())
                {
                    return false;
                }

                CurrentRow = _rowBuffer.ToRow(RowIndex, LineNumber);
                RowIndex++;
                return true;
            }

            consumedAnything = true;
            var ch = (char)code;

            if (inQuotes)
            {
                if (hasDistinctEscape && ch == escape)
                {
                    var escaped = await ReadCharAsync(cancellationToken).ConfigureAwait(false);

                    if (escaped == quote)
                    {
                        _rowBuffer.Append(quote);
                        continue;
                    }

                    if (escaped >= 0)
                    {
                        PushBack(escaped);
                    }

                    _rowBuffer.Append(ch);
                    continue;
                }

                if (ch == quote)
                {
                    var next = await ReadCharAsync(cancellationToken).ConfigureAwait(false);

                    if (next == quote)
                    {
                        _rowBuffer.Append(quote);
                        continue;
                    }

                    inQuotes = false;
                    afterClosingQuote = true;

                    if (next >= 0)
                    {
                        PushBack(next);
                    }

                    continue;
                }

                _rowBuffer.Append(ch);
                continue;
            }

            if (afterClosingQuote)
            {
                if (ch == delimiter)
                {
                    _rowBuffer.CompleteField(true, trimOptions);
                    fieldWasQuoted = false;
                    afterClosingQuote = false;
                    continue;
                }

                if (ch == '\r' || ch == '\n')
                {
                    await ConsumeNewLineSuffixAsync(ch, cancellationToken).ConfigureAwait(false);
                    _rowBuffer.CompleteField(true, trimOptions);
                    if (ignoreBlankLines && _rowBuffer.IsBlankLine())
                    {
                        ResetRowState(ref consumedAnything, ref inQuotes, ref afterClosingQuote, ref fieldWasQuoted);
                        continue;
                    }

                    CurrentRow = _rowBuffer.ToRow(RowIndex, LineNumber);
                    RowIndex++;
                    return true;
                }

                if (char.IsWhiteSpace(ch))
                {
                    continue;
                }

                HandleBadData(
                    _rowBuffer.FieldCount,
                    "Unexpected character after closing quote.",
                    _rowBuffer.CurrentFieldMemory);

                afterClosingQuote = false;
                _rowBuffer.Append(ch);
                continue;
            }

            if (ch == delimiter)
            {
                _rowBuffer.CompleteField(fieldWasQuoted, trimOptions);
                fieldWasQuoted = false;
                continue;
            }

            if (ch == quote)
            {
                if (_rowBuffer.CurrentFieldLength == 0)
                {
                    inQuotes = true;
                    fieldWasQuoted = true;
                    continue;
                }

                HandleBadData(
                    _rowBuffer.FieldCount,
                    "Unexpected quote in unquoted field.",
                    _rowBuffer.CurrentFieldMemory);

                _rowBuffer.Append(ch);
                continue;
            }

            if (ch == '\r' || ch == '\n')
            {
                await ConsumeNewLineSuffixAsync(ch, cancellationToken).ConfigureAwait(false);
                _rowBuffer.CompleteField(fieldWasQuoted, trimOptions);
                if (ignoreBlankLines && _rowBuffer.IsBlankLine())
                {
                    ResetRowState(ref consumedAnything, ref inQuotes, ref afterClosingQuote, ref fieldWasQuoted);
                    continue;
                }

                CurrentRow = _rowBuffer.ToRow(RowIndex, LineNumber);
                RowIndex++;
                return true;
            }

            if (_rowBuffer.CurrentFieldLength == 0 && trimStartEnabled && char.IsWhiteSpace(ch))
            {
                continue;
            }

            _rowBuffer.Append(ch);
        }
    }

    private void ResetRowState(
        ref bool consumedAnything,
        ref bool inQuotes,
        ref bool afterClosingQuote,
        ref bool fieldWasQuoted)
    {
        _rowBuffer.Reset();
        consumedAnything = false;
        inQuotes = false;
        afterClosingQuote = false;
        fieldWasQuoted = false;
    }

    private void ConsumeNewLineSuffix(char ch)
    {
        if (ch == '\r')
        {
            var next = ReadChar();

            if (next != '\n' && next >= 0)
            {
                PushBack(next);
            }

            if (DetectedNewLine is null)
            {
                DetectedNewLine = next == '\n' ? "\r\n" : "\r";
            }
        }
        else if (DetectedNewLine is null)
        {
            DetectedNewLine = "\n";
        }

        LineNumber++;
    }

    private async ValueTask ConsumeNewLineSuffixAsync(char ch, CancellationToken cancellationToken)
    {
        if (ch == '\r')
        {
            var next = await ReadCharAsync(cancellationToken).ConfigureAwait(false);

            if (next != '\n' && next >= 0)
            {
                PushBack(next);
            }

            if (DetectedNewLine is null)
            {
                DetectedNewLine = next == '\n' ? "\r\n" : "\r";
            }
        }
        else if (DetectedNewLine is null)
        {
            DetectedNewLine = "\n";
        }

        LineNumber++;
    }

    private void HandleBadData(int fieldIndex, string message, ReadOnlyMemory<char> rawField)
    {
        if (options.ReadMode == CsvReadMode.Strict)
        {
            throw new CsvException(message, RowIndex, LineNumber, fieldIndex);
        }

        options.BadDataFound?.Invoke(new CsvBadDataContext(RowIndex, LineNumber, fieldIndex, message, rawField));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PushBack(int value)
    {
        _pushback = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadChar()
    {
        if (_pushback >= 0)
        {
            var pushed = _pushback;
            _pushback = -1;
            return pushed;
        }

        if (_readPosition >= _readLength)
        {
            _readLength = input.Read(_readBuffer.AsSpan());
            _readPosition = 0;
            if (_readLength == 0)
            {
                return -1;
            }
        }

        return _readBuffer[_readPosition++];
    }

    private async ValueTask<int> ReadCharAsync(CancellationToken cancellationToken)
    {
        if (_pushback >= 0)
        {
            var pushed = _pushback;
            _pushback = -1;
            return pushed;
        }

        if (_readPosition >= _readLength)
        {
            _readLength = await input.ReadAsync(_readBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            _readPosition = 0;
            if (_readLength == 0)
            {
                return -1;
            }
        }

        return _readBuffer[_readPosition++];
    }
}
