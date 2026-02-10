using System.Globalization;
using CsvToolkit.Core.TypeConversion;

namespace CsvToolkit.Core;

/// <summary>
/// Configures parsing and writing behavior for <see cref="CsvReader"/> and <see cref="CsvWriter"/>.
/// </summary>
public sealed class CsvOptions
{
    public static CsvOptions Default { get; } = new();

    public char Delimiter { get; set; } = ',';

    public bool HasHeader { get; set; } = true;

    public char Quote { get; set; } = '"';

    public char Escape { get; set; } = '"';

    public string? NewLine { get; set; }

    public CsvTrimOptions TrimOptions { get; set; } = CsvTrimOptions.None;

    public bool DetectColumnCount { get; set; } = true;

    public bool IgnoreBlankLines { get; set; }

    public CsvReadMode ReadMode { get; set; } = CsvReadMode.Strict;

    public CultureInfo CultureInfo { get; set; } = CultureInfo.InvariantCulture;

    public StringComparer HeaderComparer { get; set; } = StringComparer.OrdinalIgnoreCase;

    public int CharBufferSize { get; set; } = 16 * 1024;

    public int ByteBufferSize { get; set; } = 16 * 1024;

    public Action<CsvBadDataContext>? BadDataFound { get; set; }

    public CsvConverterRegistry Converters { get; } = new();

    public CsvOptions Clone()
    {
        var clone = new CsvOptions
        {
            Delimiter = Delimiter,
            HasHeader = HasHeader,
            Quote = Quote,
            Escape = Escape,
            NewLine = NewLine,
            TrimOptions = TrimOptions,
            DetectColumnCount = DetectColumnCount,
            IgnoreBlankLines = IgnoreBlankLines,
            ReadMode = ReadMode,
            CultureInfo = CultureInfo,
            HeaderComparer = HeaderComparer,
            CharBufferSize = CharBufferSize,
            ByteBufferSize = ByteBufferSize,
            BadDataFound = BadDataFound
        };

        return clone.CopyConvertersFrom(this);
    }

    internal CsvOptions CopyConvertersFrom(CsvOptions source)
    {
        Converters.CopyFrom(source.Converters);
        return this;
    }

    internal void Validate()
    {
        if (Delimiter == '\0')
        {
            throw new ArgumentOutOfRangeException(nameof(Delimiter), "Delimiter cannot be null.");
        }

        if (Quote == '\0')
        {
            throw new ArgumentOutOfRangeException(nameof(Quote), "Quote cannot be null.");
        }

        if (Escape == '\0')
        {
            throw new ArgumentOutOfRangeException(nameof(Escape), "Escape cannot be null.");
        }

        if (CharBufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CharBufferSize), "Buffer size must be positive.");
        }

        if (ByteBufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ByteBufferSize), "Buffer size must be positive.");
        }
    }
}
