using System.Globalization;
using CsvToolkit.Core.TypeConversion;

namespace CsvToolkit.Core;

/// <summary>
/// Configures parsing and writing behavior for <see cref="CsvReader"/> and <see cref="CsvWriter"/>.
/// </summary>
public sealed class CsvOptions
{
    private string _delimiter = ",";
    private string[] _delimiterCandidates = [",", ";", "\t", "|"];

    public static CsvOptions Default { get; } = new();

    public char Delimiter
    {
        get => _delimiter.Length == 0 ? ',' : _delimiter[0];
        set => _delimiter = value.ToString();
    }

    public string DelimiterString
    {
        get => _delimiter;
        set => _delimiter = value ?? throw new ArgumentNullException(nameof(value));
    }

    public bool DetectDelimiter { get; set; }

    public string[] DelimiterCandidates
    {
        get => _delimiterCandidates;
        set => _delimiterCandidates = value ?? throw new ArgumentNullException(nameof(value));
    }

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

    public Action<CsvMissingFieldContext>? MissingFieldFound { get; set; }

    public Action<CsvHeaderValidationContext>? HeaderValidated { get; set; }

    public Func<CsvReadingExceptionContext, bool>? ReadingExceptionOccurred { get; set; }

    public Func<string, int, string> PrepareHeaderForMatch { get; set; } = static (header, _) => header;

    public bool SanitizeForInjection { get; set; }

    public char InjectionEscapeCharacter { get; set; } = '\'';

    public string InjectionCharacters { get; set; } = "=+-@";

    public CsvConverterRegistry Converters { get; } = new();

    public CsvTypeConverterOptionsRegistry ConverterOptions { get; } = new();

    public CsvOptions Clone()
    {
        var clone = new CsvOptions
        {
            Delimiter = Delimiter,
            DelimiterString = DelimiterString,
            DetectDelimiter = DetectDelimiter,
            DelimiterCandidates = DelimiterCandidates.ToArray(),
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
            BadDataFound = BadDataFound,
            MissingFieldFound = MissingFieldFound,
            HeaderValidated = HeaderValidated,
            ReadingExceptionOccurred = ReadingExceptionOccurred,
            PrepareHeaderForMatch = PrepareHeaderForMatch,
            SanitizeForInjection = SanitizeForInjection,
            InjectionEscapeCharacter = InjectionEscapeCharacter,
            InjectionCharacters = InjectionCharacters
        };

        return clone
            .CopyConvertersFrom(this)
            .CopyConverterOptionsFrom(this);
    }

    internal CsvOptions CopyConvertersFrom(CsvOptions source)
    {
        Converters.CopyFrom(source.Converters);
        return this;
    }

    internal CsvOptions CopyConverterOptionsFrom(CsvOptions source)
    {
        ConverterOptions.CopyFrom(source.ConverterOptions);
        return this;
    }

    internal void Validate()
    {
        if (DelimiterString.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DelimiterString), "Delimiter cannot be empty.");
        }

        if (DelimiterString.IndexOf('\0') >= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DelimiterString), "Delimiter cannot contain null.");
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

        if (PrepareHeaderForMatch is null)
        {
            throw new ArgumentNullException(nameof(PrepareHeaderForMatch));
        }

        if (InjectionEscapeCharacter == '\0')
        {
            throw new ArgumentOutOfRangeException(nameof(InjectionEscapeCharacter),
                "Injection escape character cannot be null.");
        }

        if (InjectionCharacters is null)
        {
            throw new ArgumentNullException(nameof(InjectionCharacters));
        }

        if (DelimiterCandidates.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DelimiterCandidates),
                "Delimiter candidates must contain at least one item.");
        }

        for (var i = 0; i < DelimiterCandidates.Length; i++)
        {
            var candidate = DelimiterCandidates[i];
            if (string.IsNullOrEmpty(candidate))
            {
                throw new ArgumentOutOfRangeException(nameof(DelimiterCandidates),
                    "Delimiter candidates cannot contain null or empty values.");
            }
        }
    }
}
