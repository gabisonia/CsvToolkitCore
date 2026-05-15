using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using CsvHelper.Configuration;
using CsvToolkit.Core.Mapping;
using nietras.SeparatedValues;

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
    private ConverterOptionsRecord[] _converterRecords = [];
    private string _csvConverterOptions = string.Empty;
    private byte[] _csvConverterOptionsUtf8 = [];
    private string _csvDuplicateHeaders = string.Empty;
    private byte[] _csvDuplicateHeadersUtf8 = [];
    private CsvMapRegistry _duplicateHeadersMapRegistry = new();

    [Params(10_000, 100_000)] public int RowCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _records = new BenchmarkRecord[RowCount];
        _converterRecords = new ConverterOptionsRecord[RowCount];

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

            _converterRecords[i] = new ConverterOptionsRecord
            {
                Id = i + 1,
                Flag = i % 2 == 0,
                Created = new DateTime(2025, 1, 1).AddDays(i % 365),
                Score = i % 10 == 0 ? null : (i * 7) % 1000
            };
        }

        _csvDefault = GenerateCsv(_records, ',', quoteFrequency: 0.1, newLine: "\n");
        _csvDefaultUtf8 = Encoding.UTF8.GetBytes(_csvDefault);

        _csvSemicolonQuoted = GenerateCsv(_records, ';', quoteFrequency: 0.7, newLine: "\n");
        _csvSemicolonQuotedUtf8 = Encoding.UTF8.GetBytes(_csvSemicolonQuoted);

        _csvConverterOptions = GenerateConverterOptionsCsv(_converterRecords, "\n");
        _csvConverterOptionsUtf8 = Encoding.UTF8.GetBytes(_csvConverterOptions);

        _csvDuplicateHeaders = GenerateDuplicateHeadersCsv(RowCount, "\n");
        _csvDuplicateHeadersUtf8 = Encoding.UTF8.GetBytes(_csvDuplicateHeaders);

        _duplicateHeadersMapRegistry = new CsvMapRegistry();
        _duplicateHeadersMapRegistry.Register<DuplicateHeaderRecord>(map =>
        {
            map.Map(x => x.FirstName).Name("name").NameIndex(0);
            map.Map(x => x.LastName).Name("name").NameIndex(1);
            map.Map(x => x.Age).Name("age");
        });
    }

    [Benchmark(Baseline = true)]
    public int CsvToolkitCore_ReadTyped_Stream()
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
    public async Task<int> CsvToolkitCore_ReadTypedAsync_Stream()
    {
        await using var stream = new MemoryStream(_csvDefaultUtf8, writable: false);
        await using var reader = new CsvReader(stream, new CsvOptions
        {
            HasHeader = true,
            DetectColumnCount = true,
            CultureInfo = CultureInfo.InvariantCulture
        });

        var count = 0;
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            _ = reader.GetRecord<BenchmarkRecord>();
            count++;
        }

        return count;
    }

    [Benchmark]
    public int CsvToolkitCore_ReadTyped_ManualMapping_Stream()
    {
        using var stream = new MemoryStream(_csvDefaultUtf8, writable: false);
        using var reader = new CsvReader(stream, new CsvOptions
        {
            HasHeader = true,
            DetectColumnCount = true,
            CultureInfo = CultureInfo.InvariantCulture
        });

        var idIndex = reader.GetFieldIndex(nameof(BenchmarkRecord.Id));
        var nameIndex = reader.GetFieldIndex(nameof(BenchmarkRecord.Name));
        var amountIndex = reader.GetFieldIndex(nameof(BenchmarkRecord.Amount));
        var createdAtIndex = reader.GetFieldIndex(nameof(BenchmarkRecord.CreatedAt));
        var isActiveIndex = reader.GetFieldIndex(nameof(BenchmarkRecord.IsActive));

        var count = 0;
        while (reader.TryReadRow(out var row))
        {
            _ = new BenchmarkRecord
            {
                Id = int.Parse(row.GetFieldSpan(idIndex), CultureInfo.InvariantCulture),
                Name = row.GetFieldString(nameIndex),
                Amount = decimal.Parse(row.GetFieldSpan(amountIndex), CultureInfo.InvariantCulture),
                CreatedAt = DateTime.Parse(row.GetFieldSpan(createdAtIndex), CultureInfo.InvariantCulture),
                IsActive = bool.Parse(row.GetFieldSpan(isActiveIndex))
            };
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
    public int Sep_ReadTyped_Stream()
    {
        using var stream = new MemoryStream(_csvDefaultUtf8, writable: false);
        using var reader = Sep.New(',').Reader(o => o with
        {
            HasHeader = true,
            Unescape = true,
            CultureInfo = CultureInfo.InvariantCulture
        }).From(stream);

        var count = 0;
        foreach (var row in reader)
        {
            _ = new BenchmarkRecord
            {
                Id = row[nameof(BenchmarkRecord.Id)].Parse<int>(),
                Name = row[nameof(BenchmarkRecord.Name)].ToString(),
                Amount = row[nameof(BenchmarkRecord.Amount)].Parse<decimal>(),
                CreatedAt = row[nameof(BenchmarkRecord.CreatedAt)].Parse<DateTime>(),
                IsActive = row[nameof(BenchmarkRecord.IsActive)].Parse<bool>()
            };
            count++;
        }

        return count;
    }

    [Benchmark]
    public int CsvToolkitCore_ReadDictionary_Stream()
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
    public long CsvToolkitCore_WriteTyped_Stream()
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
    public long CsvToolkitCore_WriteTyped_ManualMapping_Stream()
    {
        using var stream = new MemoryStream();
        using var writer = new CsvWriter(stream, new CsvOptions
        {
            HasHeader = true,
            NewLine = "\n",
            CultureInfo = CultureInfo.InvariantCulture
        });

        writer.WriteField(nameof(BenchmarkRecord.Id).AsSpan());
        writer.WriteField(nameof(BenchmarkRecord.Name).AsSpan());
        writer.WriteField(nameof(BenchmarkRecord.Amount).AsSpan());
        writer.WriteField(nameof(BenchmarkRecord.CreatedAt).AsSpan());
        writer.WriteField(nameof(BenchmarkRecord.IsActive).AsSpan());
        writer.NextRecord();

        foreach (var record in _records)
        {
            WriteBenchmarkRecordFields(writer, record);
        }

        writer.Flush();
        return stream.Length;
    }

    [Benchmark]
    public async Task<long> CsvToolkitCore_WriteTypedAsync_Stream()
    {
        await using var stream = new MemoryStream();
        await using var writer = new CsvWriter(stream, new CsvOptions
        {
            HasHeader = true,
            NewLine = "\n",
            CultureInfo = CultureInfo.InvariantCulture
        });

        await writer.WriteHeaderAsync<BenchmarkRecord>().ConfigureAwait(false);
        foreach (var record in _records)
        {
            await writer.WriteRecordAsync(record).ConfigureAwait(false);
        }

        await writer.FlushAsync().ConfigureAwait(false);
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
    public long Sep_WriteTyped_Stream()
    {
        using var stream = new MemoryStream();
        using var writer = Sep.New(',').Writer(o => o with { Escape = true }).To(stream, leaveOpen: true);

        foreach (var record in _records)
        {
            using var row = writer.NewRow();
            row[nameof(BenchmarkRecord.Id)].Format(record.Id);
            row[nameof(BenchmarkRecord.Name)].Set(record.Name);
            row[nameof(BenchmarkRecord.Amount)].Format(record.Amount);
            row[nameof(BenchmarkRecord.CreatedAt)].Format(record.CreatedAt, "O");
            row[nameof(BenchmarkRecord.IsActive)].Set(record.IsActive ? "true" : "false");
        }

        writer.Flush();
        return stream.Length;
    }

    [Benchmark]
    public int CsvToolkitCore_ReadTyped_SemicolonHighQuote()
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
    public int CsvToolkitCore_ReadTyped_ManualMapping_SemicolonHighQuote()
    {
        using var stream = new MemoryStream(_csvSemicolonQuotedUtf8, writable: false);
        using var reader = new CsvReader(stream, new CsvOptions
        {
            Delimiter = ';',
            HasHeader = true,
            CultureInfo = CultureInfo.InvariantCulture
        });

        var idIndex = reader.GetFieldIndex(nameof(BenchmarkRecord.Id));
        var nameIndex = reader.GetFieldIndex(nameof(BenchmarkRecord.Name));
        var amountIndex = reader.GetFieldIndex(nameof(BenchmarkRecord.Amount));
        var createdAtIndex = reader.GetFieldIndex(nameof(BenchmarkRecord.CreatedAt));
        var isActiveIndex = reader.GetFieldIndex(nameof(BenchmarkRecord.IsActive));

        var count = 0;
        while (reader.TryReadRow(out var row))
        {
            _ = new BenchmarkRecord
            {
                Id = int.Parse(row.GetFieldSpan(idIndex), CultureInfo.InvariantCulture),
                Name = row.GetFieldString(nameIndex),
                Amount = decimal.Parse(row.GetFieldSpan(amountIndex), CultureInfo.InvariantCulture),
                CreatedAt = DateTime.Parse(row.GetFieldSpan(createdAtIndex), CultureInfo.InvariantCulture),
                IsActive = bool.Parse(row.GetFieldSpan(isActiveIndex))
            };
            count++;
        }

        return count;
    }

    [Benchmark]
    public async Task<int> CsvToolkitCore_ReadTypedAsync_SemicolonHighQuote()
    {
        await using var stream = new MemoryStream(_csvSemicolonQuotedUtf8, writable: false);
        await using var reader = new CsvReader(stream, new CsvOptions
        {
            Delimiter = ';',
            HasHeader = true,
            CultureInfo = CultureInfo.InvariantCulture
        });

        var count = 0;
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            _ = reader.GetRecord<BenchmarkRecord>();
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

    [Benchmark]
    public int Sep_ReadTyped_SemicolonHighQuote()
    {
        using var stream = new MemoryStream(_csvSemicolonQuotedUtf8, writable: false);
        using var reader = Sep.New(';').Reader(o => o with
        {
            HasHeader = true,
            Unescape = true,
            CultureInfo = CultureInfo.InvariantCulture
        }).From(stream);

        var count = 0;
        foreach (var row in reader)
        {
            _ = new BenchmarkRecord
            {
                Id = row[nameof(BenchmarkRecord.Id)].Parse<int>(),
                Name = row[nameof(BenchmarkRecord.Name)].ToString(),
                Amount = row[nameof(BenchmarkRecord.Amount)].Parse<decimal>(),
                CreatedAt = row[nameof(BenchmarkRecord.CreatedAt)].Parse<DateTime>(),
                IsActive = row[nameof(BenchmarkRecord.IsActive)].Parse<bool>()
            };
            count++;
        }

        return count;
    }

    [Benchmark]
    public int CsvToolkitCore_ReadTyped_WithConverterOptions_Stream()
    {
        using var stream = new MemoryStream(_csvConverterOptionsUtf8, writable: false);
        var options = new CsvOptions
        {
            HasHeader = true,
            CultureInfo = CultureInfo.InvariantCulture
        };
        options.ConverterOptions.Configure<bool>(o => o.AddTrueValues("Y").AddFalseValues("N"));
        options.ConverterOptions.Configure<DateTime>(o => o.AddFormats("dd-MM-yyyy"));
        options.ConverterOptions.Configure<int?>(o => o.AddNullValues("NULL"));
        using var reader = new CsvReader(stream, options);

        var count = 0;
        while (reader.TryReadRecord<ConverterOptionsRecord>(out _))
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int CsvToolkitCore_ReadTyped_ManualMapping_WithConverterOptions_Stream()
    {
        using var stream = new MemoryStream(_csvConverterOptionsUtf8, writable: false);
        using var reader = new CsvReader(stream, new CsvOptions
        {
            HasHeader = true,
            CultureInfo = CultureInfo.InvariantCulture
        });

        var idIndex = reader.GetFieldIndex(nameof(ConverterOptionsRecord.Id));
        var flagIndex = reader.GetFieldIndex(nameof(ConverterOptionsRecord.Flag));
        var createdIndex = reader.GetFieldIndex(nameof(ConverterOptionsRecord.Created));
        var scoreIndex = reader.GetFieldIndex(nameof(ConverterOptionsRecord.Score));

        var count = 0;
        while (reader.TryReadRow(out var row))
        {
            _ = new ConverterOptionsRecord
            {
                Id = int.Parse(row.GetFieldSpan(idIndex), CultureInfo.InvariantCulture),
                Flag = ParseYN(row.GetFieldSpan(flagIndex)),
                Created = DateTime.ParseExact(row.GetFieldSpan(createdIndex), "dd-MM-yyyy", CultureInfo.InvariantCulture),
                Score = ParseNullableInt(row.GetFieldSpan(scoreIndex))
            };
            count++;
        }

        return count;
    }

    [Benchmark]
    public async Task<int> CsvToolkitCore_ReadTypedAsync_WithConverterOptions_Stream()
    {
        await using var stream = new MemoryStream(_csvConverterOptionsUtf8, writable: false);
        var options = new CsvOptions
        {
            HasHeader = true,
            CultureInfo = CultureInfo.InvariantCulture
        };
        options.ConverterOptions.Configure<bool>(o => o.AddTrueValues("Y").AddFalseValues("N"));
        options.ConverterOptions.Configure<DateTime>(o => o.AddFormats("dd-MM-yyyy"));
        options.ConverterOptions.Configure<int?>(o => o.AddNullValues("NULL"));
        await using var reader = new CsvReader(stream, options);

        var count = 0;
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            _ = reader.GetRecord<ConverterOptionsRecord>();
            count++;
        }

        return count;
    }

    [Benchmark]
    public int CsvHelper_ReadTyped_WithConverterOptions_Stream()
    {
        using var stream = new MemoryStream(_csvConverterOptionsUtf8, writable: false);
        using var textReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
            bufferSize: 16 * 1024, leaveOpen: false);
        using var csv = new CsvHelper.CsvReader(textReader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ","
        });

        var booleanOptions = csv.Context.TypeConverterOptionsCache.GetOptions<bool>();
        booleanOptions.BooleanTrueValues.Add("Y");
        booleanOptions.BooleanFalseValues.Add("N");

        var dateOptions = csv.Context.TypeConverterOptionsCache.GetOptions<DateTime>();
        dateOptions.Formats = ["dd-MM-yyyy"];

        var nullableIntOptions = csv.Context.TypeConverterOptionsCache.GetOptions<int?>();
        nullableIntOptions.NullValues.Add("NULL");

        var count = 0;
        foreach (var _ in csv.GetRecords<ConverterOptionsRecord>())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int Sep_ReadTyped_WithConverterOptions_Stream()
    {
        using var stream = new MemoryStream(_csvConverterOptionsUtf8, writable: false);
        using var reader = Sep.New(',').Reader(o => o with
        {
            HasHeader = true,
            CultureInfo = CultureInfo.InvariantCulture
        }).From(stream);

        var count = 0;
        foreach (var row in reader)
        {
            _ = new ConverterOptionsRecord
            {
                Id = row[nameof(ConverterOptionsRecord.Id)].Parse<int>(),
                Flag = ParseYN(row[nameof(ConverterOptionsRecord.Flag)].Span),
                Created = DateTime.ParseExact(row[nameof(ConverterOptionsRecord.Created)].Span, "dd-MM-yyyy",
                    CultureInfo.InvariantCulture),
                Score = ParseNullableInt(row[nameof(ConverterOptionsRecord.Score)].Span)
            };
            count++;
        }

        return count;
    }

    [Benchmark]
    public long CsvToolkitCore_WriteTyped_WithConverterOptions_Stream()
    {
        using var stream = new MemoryStream();
        var options = new CsvOptions
        {
            HasHeader = true,
            NewLine = "\n",
            CultureInfo = CultureInfo.InvariantCulture
        };
        options.ConverterOptions.Configure<bool>(o => o.AddTrueValues("Y").AddFalseValues("N"));
        options.ConverterOptions.Configure<DateTime>(o => o.AddFormats("dd-MM-yyyy"));
        options.ConverterOptions.Configure<int?>(o => o.AddNullValues("NULL"));
        using var writer = new CsvWriter(stream, options);

        writer.WriteHeader<ConverterOptionsRecord>();
        foreach (var record in _converterRecords)
        {
            writer.WriteRecord(record);
        }

        writer.Flush();
        return stream.Length;
    }

    [Benchmark]
    public long CsvToolkitCore_WriteTyped_ManualMapping_WithConverterOptions_Stream()
    {
        using var stream = new MemoryStream();
        using var writer = new CsvWriter(stream, new CsvOptions
        {
            HasHeader = true,
            NewLine = "\n",
            CultureInfo = CultureInfo.InvariantCulture
        });

        writer.WriteField(nameof(ConverterOptionsRecord.Id).AsSpan());
        writer.WriteField(nameof(ConverterOptionsRecord.Flag).AsSpan());
        writer.WriteField(nameof(ConverterOptionsRecord.Created).AsSpan());
        writer.WriteField(nameof(ConverterOptionsRecord.Score).AsSpan());
        writer.NextRecord();

        foreach (var record in _converterRecords)
        {
            WriteConverterOptionsRecordFields(writer, record);
        }

        writer.Flush();
        return stream.Length;
    }

    [Benchmark]
    public async Task<long> CsvToolkitCore_WriteTypedAsync_WithConverterOptions_Stream()
    {
        await using var stream = new MemoryStream();
        var options = new CsvOptions
        {
            HasHeader = true,
            NewLine = "\n",
            CultureInfo = CultureInfo.InvariantCulture
        };
        options.ConverterOptions.Configure<bool>(o => o.AddTrueValues("Y").AddFalseValues("N"));
        options.ConverterOptions.Configure<DateTime>(o => o.AddFormats("dd-MM-yyyy"));
        options.ConverterOptions.Configure<int?>(o => o.AddNullValues("NULL"));
        await using var writer = new CsvWriter(stream, options);

        await writer.WriteHeaderAsync<ConverterOptionsRecord>().ConfigureAwait(false);
        foreach (var record in _converterRecords)
        {
            await writer.WriteRecordAsync(record).ConfigureAwait(false);
        }

        await writer.FlushAsync().ConfigureAwait(false);
        return stream.Length;
    }

    [Benchmark]
    public long CsvHelper_WriteTyped_WithConverterOptions_Stream()
    {
        using var stream = new MemoryStream();
        using var textWriter = new StreamWriter(stream, Encoding.UTF8, 16 * 1024, leaveOpen: true);
        using var csv = new CsvHelper.CsvWriter(textWriter, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ",",
            NewLine = "\n"
        });

        var booleanOptions = csv.Context.TypeConverterOptionsCache.GetOptions<bool>();
        booleanOptions.BooleanTrueValues.Add("Y");
        booleanOptions.BooleanFalseValues.Add("N");

        var dateOptions = csv.Context.TypeConverterOptionsCache.GetOptions<DateTime>();
        dateOptions.Formats = ["dd-MM-yyyy"];

        var nullableIntOptions = csv.Context.TypeConverterOptionsCache.GetOptions<int?>();
        nullableIntOptions.NullValues.Add("NULL");

        csv.WriteHeader<ConverterOptionsRecord>();
        csv.NextRecord();

        foreach (var record in _converterRecords)
        {
            csv.WriteRecord(record);
            csv.NextRecord();
        }

        textWriter.Flush();
        return stream.Length;
    }

    [Benchmark]
    public long Sep_WriteTyped_WithConverterOptions_Stream()
    {
        using var stream = new MemoryStream();
        using var writer = Sep.New(',').Writer().To(stream, leaveOpen: true);

        foreach (var record in _converterRecords)
        {
            using var row = writer.NewRow();
            row[nameof(ConverterOptionsRecord.Id)].Format(record.Id);
            row[nameof(ConverterOptionsRecord.Flag)].Set(record.Flag ? "Y" : "N");
            row[nameof(ConverterOptionsRecord.Created)].Format(record.Created, "dd-MM-yyyy");
            row[nameof(ConverterOptionsRecord.Score)].Set(
                record.Score?.ToString(CultureInfo.InvariantCulture) ?? "NULL");
        }

        writer.Flush();
        return stream.Length;
    }

    [Benchmark]
    public int CsvToolkitCore_ReadTyped_DuplicateHeader_NameIndex_Stream()
    {
        using var stream = new MemoryStream(_csvDuplicateHeadersUtf8, writable: false);
        using var reader = new CsvReader(stream, new CsvOptions
        {
            HasHeader = true,
            CultureInfo = CultureInfo.InvariantCulture
        }, _duplicateHeadersMapRegistry);

        var count = 0;
        while (reader.TryReadRecord<DuplicateHeaderRecord>(out _))
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int CsvHelper_ReadTyped_DuplicateHeader_NameIndex_Stream()
    {
        using var stream = new MemoryStream(_csvDuplicateHeadersUtf8, writable: false);
        using var textReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
            bufferSize: 16 * 1024, leaveOpen: false);
        using var csv = new CsvHelper.CsvReader(textReader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ","
        });
        csv.Context.RegisterClassMap<CsvHelperDuplicateHeaderRecordMap>();

        var count = 0;
        foreach (var _ in csv.GetRecords<DuplicateHeaderRecord>())
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

    private static string GenerateConverterOptionsCsv(IEnumerable<ConverterOptionsRecord> records, string newLine)
    {
        var builder = new StringBuilder(capacity: 32 * 1024);
        builder.Append("Id,Flag,Created,Score").Append(newLine);

        foreach (var record in records)
        {
            builder.Append(record.Id.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(record.Flag ? "Y" : "N").Append(',')
                .Append(record.Created.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)).Append(',')
                .Append(record.Score?.ToString(CultureInfo.InvariantCulture) ?? "NULL")
                .Append(newLine);
        }

        return builder.ToString();
    }

    private static string GenerateDuplicateHeadersCsv(int rowCount, string newLine)
    {
        var random = new Random(7);
        var builder = new StringBuilder(capacity: 32 * 1024);
        builder.Append("name,name,age").Append(newLine);

        for (var i = 0; i < rowCount; i++)
        {
            var firstName = random.NextDouble() < 0.1 ? $"First,{i}" : $"First{i}";
            var lastName = random.NextDouble() < 0.1 ? $"Last,{i}" : $"Last{i}";
            var age = 18 + (i % 60);

            AppendValue(builder, firstName, ',', random, quoteFrequency: 0.2);
            builder.Append(',');
            AppendValue(builder, lastName, ',', random, quoteFrequency: 0.2);
            builder.Append(',');
            builder.Append(age.ToString(CultureInfo.InvariantCulture));
            builder.Append(newLine);
        }

        return builder.ToString();
    }

    private static bool ParseYN(ReadOnlySpan<char> span)
    {
        return span.Length == 1 && span[0] == 'Y';
    }

    private static int? ParseNullableInt(ReadOnlySpan<char> span)
    {
        return span.SequenceEqual("NULL") ? null : int.Parse(span, CultureInfo.InvariantCulture);
    }

    private static void WriteBenchmarkRecordFields(CsvWriter writer, BenchmarkRecord record)
    {
        Span<char> buffer = stackalloc char[64];

        record.Id.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture);
        writer.WriteField(buffer[..written]);

        writer.WriteField(record.Name.AsSpan());

        record.Amount.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture);
        writer.WriteField(buffer[..written]);

        record.CreatedAt.TryFormat(buffer, out written, "O", CultureInfo.InvariantCulture);
        writer.WriteField(buffer[..written]);

        writer.WriteField(record.IsActive ? "true".AsSpan() : "false".AsSpan());
        writer.NextRecord();
    }

    private static void WriteConverterOptionsRecordFields(CsvWriter writer, ConverterOptionsRecord record)
    {
        Span<char> buffer = stackalloc char[32];

        record.Id.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture);
        writer.WriteField(buffer[..written]);

        writer.WriteField(record.Flag ? "Y".AsSpan() : "N".AsSpan());

        record.Created.TryFormat(buffer, out written, "dd-MM-yyyy", CultureInfo.InvariantCulture);
        writer.WriteField(buffer[..written]);

        if (record.Score.HasValue)
        {
            record.Score.Value.TryFormat(buffer, out written, default, CultureInfo.InvariantCulture);
            writer.WriteField(buffer[..written]);
        }
        else
        {
            writer.WriteField("NULL".AsSpan());
        }

        writer.NextRecord();
    }

    private sealed class BenchmarkRecord
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public DateTime CreatedAt { get; set; }

        public bool IsActive { get; set; }
    }

    private sealed class ConverterOptionsRecord
    {
        public int Id { get; set; }

        public bool Flag { get; set; }

        public DateTime Created { get; set; }

        public int? Score { get; set; }
    }

    private sealed class DuplicateHeaderRecord
    {
        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public int Age { get; set; }
    }

    private sealed class CsvHelperDuplicateHeaderRecordMap : ClassMap<DuplicateHeaderRecord>
    {
        public CsvHelperDuplicateHeaderRecordMap()
        {
            Map(x => x.FirstName).Name("name").NameIndex(0);
            Map(x => x.LastName).Name("name").NameIndex(1);
            Map(x => x.Age).Name("age");
        }
    }
}
