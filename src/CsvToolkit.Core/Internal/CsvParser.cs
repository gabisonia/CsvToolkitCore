using System.Buffers;
using System.Runtime.CompilerServices;

namespace CsvToolkit.Core.Internal;

internal sealed class CsvParser(ICsvCharInput input, CsvOptions options) : IDisposable, IAsyncDisposable
{
    private readonly CsvRowBuffer _rowBuffer = new(options.CharBufferSize);
    private readonly char[] _readBuffer = ArrayPool<char>.Shared.Rent(options.CharBufferSize);
    private int[] _pushbackBuffer = new int[8];
    private int _pushbackCount;
    private string _activeDelimiter = options.DelimiterString;
    private bool _delimiterResolved = !options.DetectDelimiter;
    private int _readPosition;
    private int _readLength;

    public CsvRow CurrentRow { get; private set; }

    private long RowIndex { get; set; }

    private long LineNumber { get; set; } = 1;

    public string? DetectedNewLine { get; private set; }

    public string? DetectedDelimiter { get; private set; }

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
        EnsureDelimiterResolved();
        _rowBuffer.Reset();

        var delimiter = _activeDelimiter;
        var delimiterFirst = delimiter[0];
        var delimiterLength = delimiter.Length;
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
        var rowStartLine = LineNumber;

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

                CurrentRow = _rowBuffer.ToRow(RowIndex, rowStartLine);
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

                if (TryAppendQuotedNewLine(ch))
                {
                    continue;
                }

                _rowBuffer.Append(ch);
                continue;
            }

            if (afterClosingQuote)
            {
                if (TryConsumeDelimiter(ch, delimiter, delimiterFirst, delimiterLength))
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
                        rowStartLine = LineNumber;
                        continue;
                    }

                    CurrentRow = _rowBuffer.ToRow(RowIndex, rowStartLine);
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

            if (TryConsumeDelimiter(ch, delimiter, delimiterFirst, delimiterLength))
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
                    rowStartLine = LineNumber;
                    continue;
                }

                CurrentRow = _rowBuffer.ToRow(RowIndex, rowStartLine);
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
        await EnsureDelimiterResolvedAsync(cancellationToken).ConfigureAwait(false);
        _rowBuffer.Reset();

        var delimiter = _activeDelimiter;
        var delimiterFirst = delimiter[0];
        var delimiterLength = delimiter.Length;
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
        var rowStartLine = LineNumber;

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

                CurrentRow = _rowBuffer.ToRow(RowIndex, rowStartLine);
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

                if (await TryAppendQuotedNewLineAsync(ch, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                _rowBuffer.Append(ch);
                continue;
            }

            if (afterClosingQuote)
            {
                if (await TryConsumeDelimiterAsync(ch, delimiter, delimiterFirst, delimiterLength, cancellationToken)
                        .ConfigureAwait(false))
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
                        rowStartLine = LineNumber;
                        continue;
                    }

                    CurrentRow = _rowBuffer.ToRow(RowIndex, rowStartLine);
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

            if (await TryConsumeDelimiterAsync(ch, delimiter, delimiterFirst, delimiterLength, cancellationToken)
                    .ConfigureAwait(false))
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
                    rowStartLine = LineNumber;
                    continue;
                }

                CurrentRow = _rowBuffer.ToRow(RowIndex, rowStartLine);
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

    private void EnsureDelimiterResolved()
    {
        if (!options.DetectDelimiter)
        {
            _activeDelimiter = options.DelimiterString;
            _delimiterResolved = true;
            return;
        }

        if (_delimiterResolved)
        {
            return;
        }

        var sample = ReadDelimiterSample();
        _activeDelimiter = DetectDelimiter(sample, options.DelimiterCandidates, options.Quote, options.Escape) ??
                           options.DelimiterString;
        DetectedDelimiter = _activeDelimiter;
        PushBackSample(sample);
        _delimiterResolved = true;
    }

    private async ValueTask EnsureDelimiterResolvedAsync(CancellationToken cancellationToken)
    {
        if (!options.DetectDelimiter)
        {
            _activeDelimiter = options.DelimiterString;
            _delimiterResolved = true;
            return;
        }

        if (_delimiterResolved)
        {
            return;
        }

        var sample = await ReadDelimiterSampleAsync(cancellationToken).ConfigureAwait(false);
        _activeDelimiter = DetectDelimiter(sample, options.DelimiterCandidates, options.Quote, options.Escape) ??
                           options.DelimiterString;
        DetectedDelimiter = _activeDelimiter;
        PushBackSample(sample);
        _delimiterResolved = true;
    }

    private List<int> ReadDelimiterSample()
    {
        var sample = new List<int>(64);
        while (true)
        {
            var code = ReadChar();
            if (code < 0)
            {
                break;
            }

            sample.Add(code);
            if (code == '\n' || code == '\r')
            {
                break;
            }
        }

        return sample;
    }

    private async ValueTask<List<int>> ReadDelimiterSampleAsync(CancellationToken cancellationToken)
    {
        var sample = new List<int>(64);
        while (true)
        {
            var code = await ReadCharAsync(cancellationToken).ConfigureAwait(false);
            if (code < 0)
            {
                break;
            }

            sample.Add(code);
            if (code == '\n' || code == '\r')
            {
                break;
            }
        }

        return sample;
    }

    private void PushBackSample(List<int> sample)
    {
        for (var i = sample.Count - 1; i >= 0; i--)
        {
            PushBack(sample[i]);
        }
    }

    private static string? DetectDelimiter(
        IReadOnlyList<int> sample,
        IReadOnlyList<string> candidates,
        char quote,
        char escape)
    {
        string? best = null;
        var bestCount = 0;

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var count = CountDelimiterOccurrences(sample, candidate, quote, escape);
            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        return bestCount > 0 ? best : null;
    }

    private static int CountDelimiterOccurrences(
        IReadOnlyList<int> sample,
        string delimiter,
        char quote,
        char escape)
    {
        var count = 0;
        var inQuotes = false;

        for (var i = 0; i < sample.Count; i++)
        {
            var ch = (char)sample[i];

            if (ch == '\r' || ch == '\n')
            {
                break;
            }

            if (ch == quote)
            {
                if (inQuotes)
                {
                    if (quote == escape && i + 1 < sample.Count && (char)sample[i + 1] == quote)
                    {
                        i++;
                        continue;
                    }

                    inQuotes = false;
                    continue;
                }

                inQuotes = true;
                continue;
            }

            if (inQuotes)
            {
                if (escape != quote && ch == escape && i + 1 < sample.Count && (char)sample[i + 1] == quote)
                {
                    i++;
                }

                continue;
            }

            if (MatchesDelimiterAt(sample, i, delimiter))
            {
                count++;
                i += delimiter.Length - 1;
            }
        }

        return count;
    }

    private static bool MatchesDelimiterAt(IReadOnlyList<int> source, int start, string delimiter)
    {
        if (start + delimiter.Length > source.Count)
        {
            return false;
        }

        for (var i = 0; i < delimiter.Length; i++)
        {
            if ((char)source[start + i] != delimiter[i])
            {
                return false;
            }
        }

        return true;
    }

    private bool TryConsumeDelimiter(char current, string delimiter, char delimiterFirst, int delimiterLength)
    {
        if (current != delimiterFirst)
        {
            return false;
        }

        if (delimiterLength == 1)
        {
            return true;
        }

        var consumed = new int[delimiterLength - 1];
        var consumedCount = 0;

        for (var i = 1; i < delimiterLength; i++)
        {
            var code = ReadChar();
            if (code < 0)
            {
                for (var j = consumedCount - 1; j >= 0; j--)
                {
                    PushBack(consumed[j]);
                }

                return false;
            }

            consumed[consumedCount++] = code;
            if ((char)code == delimiter[i])
            {
                continue;
            }

            for (var j = consumedCount - 1; j >= 0; j--)
            {
                PushBack(consumed[j]);
            }

            return false;
        }

        return true;
    }

    private async ValueTask<bool> TryConsumeDelimiterAsync(
        char current,
        string delimiter,
        char delimiterFirst,
        int delimiterLength,
        CancellationToken cancellationToken)
    {
        if (current != delimiterFirst)
        {
            return false;
        }

        if (delimiterLength == 1)
        {
            return true;
        }

        var consumed = new int[delimiterLength - 1];
        var consumedCount = 0;

        for (var i = 1; i < delimiterLength; i++)
        {
            var code = await ReadCharAsync(cancellationToken).ConfigureAwait(false);
            if (code < 0)
            {
                for (var j = consumedCount - 1; j >= 0; j--)
                {
                    PushBack(consumed[j]);
                }

                return false;
            }

            consumed[consumedCount++] = code;
            if ((char)code == delimiter[i])
            {
                continue;
            }

            for (var j = consumedCount - 1; j >= 0; j--)
            {
                PushBack(consumed[j]);
            }

            return false;
        }

        return true;
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

    private bool TryAppendQuotedNewLine(char ch)
    {
        if (ch == '\r')
        {
            var next = ReadChar();
            if (next == '\n')
            {
                _rowBuffer.Append('\r');
                _rowBuffer.Append('\n');
            }
            else
            {
                if (next >= 0)
                {
                    PushBack(next);
                }

                _rowBuffer.Append('\r');
            }

            LineNumber++;
            return true;
        }

        if (ch == '\n')
        {
            _rowBuffer.Append('\n');
            LineNumber++;
            return true;
        }

        return false;
    }

    private async ValueTask<bool> TryAppendQuotedNewLineAsync(char ch, CancellationToken cancellationToken)
    {
        if (ch == '\r')
        {
            var next = await ReadCharAsync(cancellationToken).ConfigureAwait(false);
            if (next == '\n')
            {
                _rowBuffer.Append('\r');
                _rowBuffer.Append('\n');
            }
            else
            {
                if (next >= 0)
                {
                    PushBack(next);
                }

                _rowBuffer.Append('\r');
            }

            LineNumber++;
            return true;
        }

        if (ch == '\n')
        {
            _rowBuffer.Append('\n');
            LineNumber++;
            return true;
        }

        return false;
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
            var exception = new CsvException(message, RowIndex, LineNumber, fieldIndex);
            if (options.ReadingExceptionOccurred?.Invoke(
                    new CsvReadingExceptionContext(exception, RowIndex, LineNumber, fieldIndex, rawField)) == true)
            {
                return;
            }

            throw exception;
        }

        options.BadDataFound?.Invoke(new CsvBadDataContext(RowIndex, LineNumber, fieldIndex, message, rawField));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PushBack(int value)
    {
        if (value < 0)
        {
            return;
        }

        if (_pushbackCount == _pushbackBuffer.Length)
        {
            Array.Resize(ref _pushbackBuffer, _pushbackBuffer.Length * 2);
        }

        _pushbackBuffer[_pushbackCount++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadChar()
    {
        if (_pushbackCount > 0)
        {
            return _pushbackBuffer[--_pushbackCount];
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
        if (_pushbackCount > 0)
        {
            return _pushbackBuffer[--_pushbackCount];
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
