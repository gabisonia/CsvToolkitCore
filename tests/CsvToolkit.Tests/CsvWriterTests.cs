using CsvToolkit.Core.Mapping;
using CsvToolkit.Core.TypeConversion;
using System.Globalization;
using System.Text;

namespace CsvToolkit.Core.Tests;

public sealed class CsvWriterTests
{
    [Fact]
    public void WriteField_QuotesWhenNeeded()
    {
        // Arrange
        var options = new CsvOptions { NewLine = "\n" };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteField("Ada,Lovelace");
        writer.WriteField("Mathematician");
        writer.NextRecord();
        var result = text.ToString();

        // Assert
        Assert.Equal("\"Ada,Lovelace\",Mathematician\n", result);
    }

    [Fact]
    public void WriteField_EscapesQuotes()
    {
        // Arrange
        var options = new CsvOptions { NewLine = "\n" };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteField("a\"b");
        writer.NextRecord();
        var result = text.ToString();

        // Assert
        Assert.Equal("\"a\"\"b\"\n", result);
    }

    [Fact]
    public void WriteField_QuotesLeadingAndTrailingWhitespace()
    {
        // Arrange
        var options = new CsvOptions { NewLine = "\n" };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteField(" Ada ");
        writer.NextRecord();
        var result = text.ToString();

        // Assert
        Assert.Equal("\" Ada \"\n", result);
    }

    [Fact]
    public void WriteField_UsesConfiguredEscapeCharacter()
    {
        // Arrange
        var options = new CsvOptions
        {
            Quote = '\'',
            Escape = '\\',
            NewLine = "\n"
        };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteField("It's fine");
        writer.NextRecord();
        var result = text.ToString();

        // Assert
        Assert.Equal("'It\\'s fine'\n", result);
    }

    [Fact]
    public void WriteField_NullValue_WritesEmptyField()
    {
        // Arrange
        var options = new CsvOptions { NewLine = "\n" };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteField<string?>(null);
        writer.WriteField("value");
        writer.NextRecord();
        var result = text.ToString();

        // Assert
        Assert.Equal(",value\n", result);
    }

    [Fact]
    public void WriteField_WhenInjectionSanitizationEnabled_PrefixesEscapeCharacter()
    {
        // Arrange
        var options = new CsvOptions
        {
            NewLine = "\n",
            SanitizeForInjection = true
        };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteField("=SUM(A1:A2)");
        writer.NextRecord();
        var result = text.ToString();

        // Assert
        Assert.Equal("'=SUM(A1:A2)\n", result);
    }

    [Fact]
    public void WriteField_WhenInjectionSanitizationAndQuotingAreBothNeeded_PreservesEscaping()
    {
        // Arrange
        var options = new CsvOptions
        {
            NewLine = "\n",
            SanitizeForInjection = true
        };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteField("=SUM(\"A1\",A2)");
        writer.NextRecord();
        var result = text.ToString();

        // Assert
        Assert.Equal("\"'=SUM(\"\"A1\"\",A2)\"\n", result);
    }

    [Fact]
    public void WriteField_WhenInjectionSanitizationDisabled_DoesNotChangeValue()
    {
        // Arrange
        var options = new CsvOptions
        {
            NewLine = "\n",
            SanitizeForInjection = false
        };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteField("=SUM(A1:A2)");
        writer.NextRecord();
        var result = text.ToString();

        // Assert
        Assert.Equal("=SUM(A1:A2)\n", result);
    }

    [Fact]
    public void WriteField_WithTabDelimiter_QuotesWhenNeeded()
    {
        // Arrange
        var options = new CsvOptions
        {
            Delimiter = '\t',
            NewLine = "\n"
        };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteField("Ada\tLovelace");
        writer.WriteField("Math");
        writer.NextRecord();
        var result = text.ToString();

        // Assert
        Assert.Equal("\"Ada\tLovelace\"\tMath\n", result);
    }

    [Fact]
    public void WriteField_WithMultiCharacterDelimiter_WritesDelimiterSequence()
    {
        // Arrange
        var options = new CsvOptions
        {
            DelimiterString = "||",
            NewLine = "\n"
        };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteField("Ada");
        writer.WriteField("Math");
        writer.NextRecord();
        var result = text.ToString();

        // Assert
        Assert.Equal("Ada||Math\n", result);
    }

    [Fact]
    public void WriteField_WithMultiCharacterDelimiter_QuotesWhenSequenceAppearsInField()
    {
        // Arrange
        var options = new CsvOptions
        {
            DelimiterString = "||",
            NewLine = "\n"
        };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteField("Ada||Lovelace");
        writer.WriteField("Math");
        writer.NextRecord();
        var result = text.ToString();

        // Assert
        Assert.Equal("\"Ada||Lovelace\"||Math\n", result);
    }

    [Fact]
    public void WriteHeaderAndRecord_WritesPoco()
    {
        // Arrange
        var options = new CsvOptions { NewLine = "\n" };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteHeader<WriteRecord>();
        writer.WriteRecord(new WriteRecord
        {
            Id = 1,
            Name = "Ada",
            Notes = "line1\nline2"
        });
        var csv = text.ToString();

        // Assert
        Assert.Contains("Id,Name,Notes\n", csv);
        Assert.Contains("1,Ada,\"line1\nline2\"\n", csv);
    }

    [Fact]
    public void WriteRecord_UsesFluentConverter()
    {
        // Arrange
        var options = new CsvOptions { NewLine = "\n" };
        var maps = new CsvMapRegistry();
        maps.Register<WriteRecord>(map =>
        {
            map.Map(x => x.Name).Converter(new LowerCaseConverter());
        });

        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options, maps);

        // Act
        writer.WriteRecord(new WriteRecord
        {
            Id = 1,
            Name = "ADA",
            Notes = "N"
        });
        var result = text.ToString();

        // Assert
        Assert.Equal("1,ada,N\n", result);
    }

    [Fact]
    public void WriteRecord_BuiltInTypes_UsesFastPathWithoutChangingOutput()
    {
        // Arrange
        var options = new CsvOptions
        {
            NewLine = "\n",
            CultureInfo = CultureInfo.InvariantCulture
        };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteRecord(new BuiltInWriteRecord
        {
            Id = 1,
            Amount = 12.5m,
            Enabled = true,
            Name = "Ada"
        });
        var result = text.ToString();

        // Assert
        Assert.Equal("1,12.5,True,Ada\n", result);
    }

    [Fact]
    public void WriteRecord_BuiltInTypes_StreamOutput_PreservesInvariantFormatting()
    {
        // Arrange
        var options = new CsvOptions
        {
            NewLine = "\n",
            CultureInfo = CultureInfo.InvariantCulture
        };
        using var stream = new MemoryStream();
        using var writer = new CsvWriter(stream, options, leaveOpen: true);

        // Act
        writer.WriteRecord(new BuiltInStreamWriteRecord
        {
            Id = 1,
            Amount = 12.5m,
            CreatedAt = new DateTime(2025, 1, 2, 3, 4, 5),
            Enabled = true,
            Name = "Ada"
        });
        writer.Flush();
        var csv = Encoding.UTF8.GetString(stream.ToArray());

        // Assert
        Assert.Equal("1,12.5,01/02/2025 03:04:05,True,Ada\n", csv);
    }

    [Fact]
    public async Task StreamWriteAndReadAsync_RoundTrips()
    {
        // Arrange
        var options = new CsvOptions { NewLine = "\n", HasHeader = false };
        await using var stream = new MemoryStream();

        // Act
        await using (var writer = new CsvWriter(stream, options, leaveOpen: true))
        {
            await writer.WriteFieldAsync("1".AsMemory());
            await writer.WriteFieldAsync("Ada".AsMemory());
            await writer.NextRecordAsync();
            await writer.FlushAsync();
        }

        stream.Position = 0;

        await using var reader = new CsvReader(stream, options, leaveOpen: true);
        var read = await reader.ReadAsync();
        var first = reader.GetField(0);
        var second = reader.GetField(1);

        // Assert
        Assert.True(read);
        Assert.Equal("1", first);
        Assert.Equal("Ada", second);
    }

    [Fact]
    public async Task AsyncApis_RespectCancellationToken()
    {
        // Arrange
        using var text = new StringWriter();
        await using var writer = new CsvWriter(text, new CsvOptions { NewLine = "\n" });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => writer.WriteFieldAsync("value".AsMemory(), cts.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => writer.NextRecordAsync(cts.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => writer.WriteHeaderAsync<WriteRecord>(cts.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => writer.WriteRecordAsync(new WriteRecord(), cts.Token).AsTask());
    }

    [Fact]
    public void WriteRecord_UsesConstantAndDefaultFromFluentMap()
    {
        // Arrange
        var options = new CsvOptions { NewLine = "\n" };
        var maps = new CsvMapRegistry();
        maps.Register<WriteRecord>(map =>
        {
            map.Map(x => x.Name).Constant("FixedName");
            map.Map(x => x.Notes).Default("DefaultNote");
        });

        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options, maps);

        // Act
        writer.WriteRecord(new WriteRecord
        {
            Id = 7,
            Name = "Ignored",
            Notes = null!
        });
        var result = text.ToString();

        // Assert
        Assert.Equal("7,FixedName,DefaultNote\n", result);
    }

    [Fact]
    public void WriteRecord_WithValidationFailure_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new CsvOptions { NewLine = "\n" };
        var maps = new CsvMapRegistry();
        maps.Register<WriteRecord>(map =>
        {
            map.Map(x => x.Id).Validate(id => id > 0, "Id must be positive.");
        });

        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options, maps);

        // Act
        Action act = () => writer.WriteRecord(new WriteRecord
        {
            Id = 0,
            Name = "Ada",
            Notes = "N"
        });

        // Assert
        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Equal("Id must be positive.", exception.Message);
    }

    [Fact]
    public void WriteRecords_WritesHeaderAndRecords()
    {
        // Arrange
        var options = new CsvOptions { NewLine = "\n" };
        var records = new[]
        {
            new WriteRecord { Id = 1, Name = "Ada", Notes = "N1" },
            new WriteRecord { Id = 2, Name = "Bob", Notes = "N2" }
        };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteRecords(records, writeHeader: true);
        var csv = text.ToString();

        // Assert
        Assert.Equal("Id,Name,Notes\n1,Ada,N1\n2,Bob,N2\n", csv);
    }

    [Fact]
    public async Task WriteRecordsAsync_Enumerable_WritesRecords()
    {
        // Arrange
        var options = new CsvOptions { NewLine = "\n" };
        var records = new[]
        {
            new WriteRecord { Id = 1, Name = "Ada", Notes = "N1" },
            new WriteRecord { Id = 2, Name = "Bob", Notes = "N2" }
        };
        using var text = new StringWriter();
        await using var writer = new CsvWriter(text, options);

        // Act
        await writer.WriteRecordsAsync(records, writeHeader: true);
        var csv = text.ToString();

        // Assert
        Assert.Equal("Id,Name,Notes\n1,Ada,N1\n2,Bob,N2\n", csv);
    }

    [Fact]
    public async Task WriteRecordsAsync_AsyncEnumerable_WritesRecords()
    {
        // Arrange
        var options = new CsvOptions { NewLine = "\n" };
        using var text = new StringWriter();
        await using var writer = new CsvWriter(text, options);

        // Act
        await writer.WriteRecordsAsync(GetAsyncRecords(), writeHeader: true);
        var csv = text.ToString();

        // Assert
        Assert.Equal("Id,Name,Notes\n1,Ada,N1\n2,Bob,N2\n", csv);
    }

    [Fact]
    public void WriteRecord_UsesGlobalConverterOptionsForBooleanAndDateFormats()
    {
        // Arrange
        var options = new CsvOptions
        {
            NewLine = "\n"
        };
        options.ConverterOptions.Configure<bool>(o => o.AddTrueValues("YES").AddFalseValues("NO"));
        options.ConverterOptions.Configure<DateTime>(o => o.AddFormats("yyyyMMdd"));
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteRecord(new ConverterOptionsWriteRecord
        {
            Flag = true,
            Created = new DateTime(2025, 12, 31),
            Note = null
        });
        var csv = text.ToString();

        // Assert
        Assert.Equal("YES,20251231,\n", csv);
    }

    [Fact]
    public void WriteRecord_ConverterOptionsFastPath_PreservesOutput()
    {
        // Arrange
        var options = new CsvOptions
        {
            NewLine = "\n",
            CultureInfo = CultureInfo.InvariantCulture
        };
        options.ConverterOptions.Configure<bool>(o => o.AddTrueValues("Y").AddFalseValues("N"));
        options.ConverterOptions.Configure<DateTime>(o => o.AddFormats("dd-MM-yyyy"));
        options.ConverterOptions.Configure<int?>(o => o.AddNullValues("NULL"));
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteRecord(new ConverterOptionsFastPathWriteRecord
        {
            Id = 7,
            Flag = true,
            Created = new DateTime(2025, 12, 31),
            Score = null
        });
        var csv = text.ToString();

        // Assert
        Assert.Equal("7,Y,31-12-2025,NULL\n", csv);
    }

    [Fact]
    public void WriteRecord_UsesConfiguredNullValueToken()
    {
        // Arrange
        var options = new CsvOptions
        {
            NewLine = "\n"
        };
        options.ConverterOptions.Configure<string>(o => o.AddNullValues("NULL"));
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteRecord(new NullTokenWriteRecord
        {
            Value = null
        });
        var csv = text.ToString();

        // Assert
        Assert.Equal("NULL\n", csv);
    }

    [Fact]
    public void WriteRecord_UsesAttributeConverterOptions()
    {
        // Arrange
        var options = new CsvOptions
        {
            NewLine = "\n"
        };
        options.ConverterOptions.Configure<bool>(o => o.AddTrueValues("TRUE").AddFalseValues("FALSE"));
        options.ConverterOptions.Configure<DateTime>(o => o.AddFormats("yyyy-MM-dd"));
        options.ConverterOptions.Configure<string>(o => o.AddNullValues("<null>"));
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        writer.WriteRecord(new AttributeConverterOptionsWriteRecord
        {
            Flag = true,
            Created = new DateTime(2025, 12, 31),
            Note = null
        });
        var csv = text.ToString();

        // Assert
        Assert.Equal("Y,31122025,NULL\n", csv);
    }

    [Fact]
    public void WriteRecord_WithAttributeValidation_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new CsvOptions { NewLine = "\n" };
        using var text = new StringWriter();
        using var writer = new CsvWriter(text, options);

        // Act
        Action act = () => writer.WriteRecord(new AttributeValidationWriteRecord
        {
            Id = 0
        });

        // Assert
        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Equal("Id must be positive.", exception.Message);
    }

    [Fact]
    public void WriteRecord_Null_ThrowsArgumentNullException()
    {
        // Arrange
        using var text = new StringWriter();
        using var writer = new CsvWriter(text);

        // Act
        Action act = () => writer.WriteRecord<WriteRecord>(null!);

        // Assert
        Assert.Throws<ArgumentNullException>(act);
    }

    private sealed class LowerCaseConverter : ICsvTypeConverter<string>
    {
        public bool TryParse(ReadOnlySpan<char> source, in CsvConverterContext context, out string value)
        {
            value = source.ToString();
            return true;
        }

        public string Format(string value, in CsvConverterContext context)
        {
            return value.ToLowerInvariant();
        }
    }

    private sealed class WriteRecord
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;
    }

    private sealed class BuiltInWriteRecord
    {
        public int Id { get; set; }

        public decimal Amount { get; set; }

        public bool Enabled { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class BuiltInStreamWriteRecord
    {
        public int Id { get; set; }

        public decimal Amount { get; set; }

        public DateTime CreatedAt { get; set; }

        public bool Enabled { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private static async IAsyncEnumerable<WriteRecord> GetAsyncRecords()
    {
        yield return new WriteRecord { Id = 1, Name = "Ada", Notes = "N1" };
        await Task.Yield();
        yield return new WriteRecord { Id = 2, Name = "Bob", Notes = "N2" };
    }

    private sealed class ConverterOptionsWriteRecord
    {
        public bool Flag { get; set; }

        public DateTime Created { get; set; }

        public string? Note { get; set; }
    }

    private sealed class NullTokenWriteRecord
    {
        public string? Value { get; set; }
    }

    private sealed class ConverterOptionsFastPathWriteRecord
    {
        public int Id { get; set; }

        public bool Flag { get; set; }

        public DateTime Created { get; set; }

        public int? Score { get; set; }
    }

    private sealed class AttributeConverterOptionsWriteRecord
    {
        [CsvTrueValues("Y"), CsvFalseValues("N")]
        public bool Flag { get; set; }

        [CsvFormats("ddMMyyyy")]
        public DateTime Created { get; set; }

        [CsvNullValues("NULL")]
        public string? Note { get; set; }
    }

    private sealed class AttributeValidationWriteRecord
    {
        [CsvValidate(typeof(AttributeValidation), nameof(AttributeValidation.IsPositive), Message = "Id must be positive.")]
        public int Id { get; set; }
    }

    private static class AttributeValidation
    {
        public static bool IsPositive(int value)
        {
            return value > 0;
        }
    }
}
