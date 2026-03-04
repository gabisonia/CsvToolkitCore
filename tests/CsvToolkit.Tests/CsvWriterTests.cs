using CsvToolkit.Core.Mapping;
using CsvToolkit.Core.TypeConversion;

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
}
