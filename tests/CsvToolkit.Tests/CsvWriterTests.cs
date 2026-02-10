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
