using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using CsvHelper.Configuration;

namespace CsvToolkit.Core.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[SimpleJob]
public class CsvReadWriteBenchmarks
{
    private BenchmarkRecord[] _records = [];
    private string _csvDefault = string.Empty;
    private byte[] _csvDefaultUtf8 = [];
    private string _csvSemicolonQuoted = string.Empty;
    private byte[] _csvSemicolonQuotedUtf8 = [];

    [Params(100_000)] public int RowCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _records = new BenchmarkRecord[RowCount];

        for (var i = 0; i < RowCount; i++)
        {
            _records[i] = new BenchmarkRecord
            {
                Id = i + 1,
                Name = random.NextDouble() < 0.1
                    ? $"User,{i}"
                    : $"User{i}",
                Amount = Math.Round((decimal)random.NextDouble() * 1000m, 2),
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                IsActive = i % 2 == 0
            };
        }

        _csvDefault = GenerateCsv(_records, ',', quoteFrequency: 0.1, newLine: "\n");
        _csvDefaultUtf8 = Encoding.UTF8.GetBytes(_csvDefault);

        _csvSemicolonQuoted = GenerateCsv(_records, ';', quoteFrequency: 0.7, newLine: "\n");
        _csvSemicolonQuotedUtf8 = Encoding.UTF8.GetBytes(_csvSemicolonQuoted);
    }

    [Benchmark(Baseline = true)]
    public int CsvToolkit_ReadTyped_Stream()
    {
        using var stream = new MemoryStream(_csvDefaultUtf8, writable: false);
        using var reader = new CsvReader(stream, new CsvOptions
        {
            HasHeader = true,
            DetectColumnCount = true,
            CultureInfo = CultureInfo.InvariantCulture
        });

        var count = 0;
        while (reader.TryReadRecord<BenchmarkRecord>(out _))
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int CsvHelper_ReadTyped_Stream()
    {
        using var stream = new MemoryStream(_csvDefaultUtf8, writable: false);
        using var textReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
            bufferSize: 16 * 1024, leaveOpen: false);
        using var csv = new CsvHelper.CsvReader(textReader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ","
        });

        var count = 0;
        foreach (var _ in csv.GetRecords<BenchmarkRecord>())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int CsvToolkit_ReadDictionary_Stream()
    {
        using var stream = new MemoryStream(_csvDefaultUtf8, writable: false);
        using var reader = new CsvReader(stream, new CsvOptions
        {
            HasHeader = true,
            DetectColumnCount = true
        });

        var count = 0;
        while (reader.TryReadDictionary(out _))
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int CsvHelper_ReadDynamic_Stream()
    {
        using var stream = new MemoryStream(_csvDefaultUtf8, writable: false);
        using var textReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
            bufferSize: 16 * 1024, leaveOpen: false);
        using var csv = new CsvHelper.CsvReader(textReader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ","
        });

        var count = 0;
        while (csv.Read())
        {
            _ = csv.GetRecord<dynamic>();
            count++;
        }

        return count;
    }

    [Benchmark]
    public long CsvToolkit_WriteTyped_Stream()
    {
        using var stream = new MemoryStream();
        using var writer = new CsvWriter(stream, new CsvOptions
        {
            HasHeader = true,
            NewLine = "\n",
            CultureInfo = CultureInfo.InvariantCulture
        });

        writer.WriteHeader<BenchmarkRecord>();
        foreach (var record in _records)
        {
            writer.WriteRecord(record);
        }

        writer.Flush();
        return stream.Length;
    }

    [Benchmark]
    public long CsvHelper_WriteTyped_Stream()
    {
        using var stream = new MemoryStream();
        using var textWriter = new StreamWriter(stream, Encoding.UTF8, 16 * 1024, leaveOpen: true);
        using var csv = new CsvHelper.CsvWriter(textWriter, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ",",
            NewLine = "\n"
        });

        csv.WriteHeader<BenchmarkRecord>();
        csv.NextRecord();

        foreach (var record in _records)
        {
            csv.WriteRecord(record);
            csv.NextRecord();
        }

        textWriter.Flush();
        return stream.Length;
    }

    [Benchmark]
    public int CsvToolkit_ReadTyped_SemicolonHighQuote()
    {
        using var stream = new MemoryStream(_csvSemicolonQuotedUtf8, writable: false);
        using var reader = new CsvReader(stream, new CsvOptions
        {
            Delimiter = ';',
            HasHeader = true,
            CultureInfo = CultureInfo.InvariantCulture
        });

        var count = 0;
        while (reader.TryReadRecord<BenchmarkRecord>(out _))
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int CsvHelper_ReadTyped_SemicolonHighQuote()
    {
        using var stream = new MemoryStream(_csvSemicolonQuotedUtf8, writable: false);
        using var textReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
            bufferSize: 16 * 1024, leaveOpen: false);
        using var csv = new CsvHelper.CsvReader(textReader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ";"
        });

        var count = 0;
        foreach (var _ in csv.GetRecords<BenchmarkRecord>())
        {
            count++;
        }

        return count;
    }

    private static string GenerateCsv(IEnumerable<BenchmarkRecord> records, char delimiter, double quoteFrequency,
        string newLine)
    {
        var random = new Random(123);
        var builder = new StringBuilder(capacity: 32 * 1024);
        builder.Append("Id").Append(delimiter)
            .Append("Name").Append(delimiter)
            .Append("Amount").Append(delimiter)
            .Append("CreatedAt").Append(delimiter)
            .Append("IsActive").Append(newLine);

        foreach (var record in records)
        {
            AppendValue(builder, record.Id.ToString(CultureInfo.InvariantCulture), delimiter, random, quoteFrequency);
            builder.Append(delimiter);
            AppendValue(builder, record.Name, delimiter, random, quoteFrequency);
            builder.Append(delimiter);
            AppendValue(builder, record.Amount.ToString(CultureInfo.InvariantCulture), delimiter, random,
                quoteFrequency);
            builder.Append(delimiter);
            AppendValue(builder, record.CreatedAt.ToString("O", CultureInfo.InvariantCulture), delimiter, random,
                quoteFrequency);
            builder.Append(delimiter);
            AppendValue(builder, record.IsActive ? "true" : "false", delimiter, random, quoteFrequency);
            builder.Append(newLine);
        }

        return builder.ToString();
    }

    private static void AppendValue(StringBuilder builder, string value, char delimiter, Random random,
        double quoteFrequency)
    {
        var forceQuote = random.NextDouble() < quoteFrequency;
        var shouldQuote = forceQuote || value.Contains(delimiter) || value.Contains('"') || value.Contains('\n') ||
                          value.Contains('\r');

        if (!shouldQuote)
        {
            builder.Append(value);
            return;
        }

        builder.Append('"');
        foreach (var ch in value)
        {
            if (ch == '"')
            {
                builder.Append('"').Append('"');
            }
            else
            {
                builder.Append(ch);
            }
        }

        builder.Append('"');
    }

    private sealed class BenchmarkRecord
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public DateTime CreatedAt { get; set; }

        public bool IsActive { get; set; }
    }
}