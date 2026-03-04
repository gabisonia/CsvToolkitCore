using System.Globalization;

namespace CsvToolkit.Core.Tests.Parsing;

public sealed class CsvReaderParsingTests
{
    [Fact]
    public void TryReadRow_HandlesQuotedDelimiter()
    {
        // Arrange
        const string csv = "id,name\n1,\"Ada,Lovelace\"\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.TryReadRow(out var row);

        // Assert
        Assert.True(read);
        Assert.Equal("1", row.GetFieldString(0));
        Assert.Equal("Ada,Lovelace", row.GetFieldString(1));
    }

    [Fact]
    public void TryReadRow_HandlesEmbeddedNewLineInsideQuotes()
    {
        // Arrange
        const string csv = "id,notes\n1,\"line1\nline2\"\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.TryReadRow(out var row);

        // Assert
        Assert.True(read);
        Assert.Equal("line1\nline2", row.GetFieldString(1));
    }

    [Fact]
    public void TryReadRow_HandlesEscapedQuotes()
    {
        // Arrange
        const string csv = "id,text\n1,\"a \"\"quote\"\" b\"\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.TryReadRow(out var row);

        // Assert
        Assert.True(read);
        Assert.Equal("a \"quote\" b", row.GetFieldString(1));
    }

    [Fact]
    public void TryReadRow_SupportsCustomDelimiter()
    {
        // Arrange
        const string csv = "id;name\n1;Ada\n";
        var options = new CsvOptions { Delimiter = ';' };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.TryReadRow(out var row);

        // Assert
        Assert.True(read);
        Assert.Equal("Ada", row.GetFieldString(1));
    }

    [Fact]
    public void TryReadRow_SupportsMultiCharacterDelimiter()
    {
        // Arrange
        const string csv = "id||name||age\n1||Ada||36\n";
        var options = new CsvOptions { DelimiterString = "||" };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.TryReadRow(out var row);

        // Assert
        Assert.True(read);
        Assert.Equal("1", row.GetFieldString(0));
        Assert.Equal("Ada", row.GetFieldString(1));
        Assert.Equal("36", row.GetFieldString(2));
    }

    [Fact]
    public void TryReadRow_DetectsDelimiterFromCandidates()
    {
        // Arrange
        const string csv = "id;name;age\n1;Ada;36\n";
        var options = new CsvOptions
        {
            Delimiter = ',',
            DetectDelimiter = true,
            DelimiterCandidates = [",", ";", "\t"]
        };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();
        var name = reader.GetField(1);

        // Assert
        Assert.True(read);
        Assert.Equal("Ada", name);
        Assert.Equal(";", reader.DetectedDelimiter);
    }

    [Theory]
    [InlineData(';', "id;name\n1;Ada\n")]
    [InlineData('|', "id|name\n1|Ada\n")]
    [InlineData('\t', "id\tname\n1\tAda\n")]
    public void TryReadRow_SupportsMultipleDelimiters(char delimiter, string csv)
    {
        // Arrange
        var options = new CsvOptions { Delimiter = delimiter };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.TryReadRow(out var row);

        // Assert
        Assert.True(read);
        Assert.Equal("1", row.GetFieldString(0));
        Assert.Equal("Ada", row.GetFieldString(1));
    }

    [Fact]
    public void TryReadRow_SupportsCustomQuoteAndEscape()
    {
        // Arrange
        const string csv = "id;name;note\n1;'Ada;Lovelace';'It\\'s fine'\n";
        var options = new CsvOptions
        {
            Delimiter = ';',
            Quote = '\'',
            Escape = '\\'
        };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.TryReadRow(out var row);

        // Assert
        Assert.True(read);
        Assert.Equal("Ada;Lovelace", row.GetFieldString(1));
        Assert.Equal("It's fine", row.GetFieldString(2));
    }

    [Fact]
    public void TryReadDictionary_UsesHeaderNames()
    {
        // Arrange
        const string csv = "id,name\n1,Ada\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.TryReadDictionary(out var values);

        // Assert
        Assert.True(read);
        Assert.NotNull(values);
        Assert.Equal("1", values["id"]);
        Assert.Equal("Ada", values["name"]);
    }

    [Fact]
    public async Task ReadAsync_ReadsRows()
    {
        // Arrange
        const string csv = "id,name\n1,Ada\n2,Bob\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var firstRead = await reader.ReadAsync();
        var firstName = reader.GetField(1);
        var secondRead = await reader.ReadAsync();
        var secondName = reader.GetField(1);
        var eofRead = await reader.ReadAsync();

        // Assert
        Assert.True(firstRead);
        Assert.Equal("Ada", firstName);
        Assert.True(secondRead);
        Assert.Equal("Bob", secondName);
        Assert.False(eofRead);
    }

    [Fact]
    public void TrimOptions_TrimStartAndEnd()
    {
        // Arrange
        const string csv = "id,name\n1,  Ada  \n";
        var options = new CsvOptions
        {
            TrimOptions = CsvTrimOptions.Trim
        };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.TryReadRow(out var row);

        // Assert
        Assert.True(read);
        Assert.Equal("Ada", row.GetFieldString(1));
    }

    [Fact]
    public void DetectColumnCount_ThrowsInStrictMode()
    {
        // Arrange
        const string csv = "a,b\n1,2\n3\n";
        var options = new CsvOptions
        {
            DetectColumnCount = true,
            ReadMode = CsvReadMode.Strict
        };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var firstRead = reader.Read();
        Action act = () => reader.Read();

        // Assert
        Assert.True(firstRead);
        var exception = Assert.Throws<CsvException>(act);
        Assert.Equal(2, exception.RowIndex);
        Assert.Equal(3, exception.LineNumber);
        Assert.Equal(1, exception.FieldIndex);
    }

    [Fact]
    public async Task DetectColumnCount_ReadAsync_ThrowsInStrictModeWithCurrentRowMetadata()
    {
        // Arrange
        const string csv = "a,b\n1,2\n3\n";
        var options = new CsvOptions
        {
            DetectColumnCount = true,
            ReadMode = CsvReadMode.Strict
        };
        await using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var firstRead = await reader.ReadAsync();
        async Task ActAsync() => await reader.ReadAsync();

        // Assert
        Assert.True(firstRead);
        var exception = await Assert.ThrowsAsync<CsvException>(ActAsync);
        Assert.Equal(2, exception.RowIndex);
        Assert.Equal(3, exception.LineNumber);
        Assert.Equal(1, exception.FieldIndex);
    }

    [Fact]
    public void LenientMode_InvokesBadDataCallback()
    {
        // Arrange
        const string csv = "a,b\n1,te\"st\n";
        var callbacks = 0;
        var options = new CsvOptions
        {
            ReadMode = CsvReadMode.Lenient,
            BadDataFound = _ => callbacks++
        };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();

        // Assert
        Assert.True(read);
        Assert.Equal(1, callbacks);
    }

    [Fact]
    public void SpanAccess_ReturnsFieldSlices()
    {
        // Arrange
        const string csv = "id,name\n1,Ada\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.Read();
        var span = reader.GetFieldSpan(1);

        // Assert
        Assert.True(read);
        Assert.True(span.SequenceEqual("Ada"));
    }

    [Fact]
    public void IgnoreBlankLines_SkipsEmptyRows()
    {
        // Arrange
        const string csv = "id,name\n1,Ada\n\n2,Bob\n";
        var options = new CsvOptions { IgnoreBlankLines = true };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var firstRead = reader.Read();
        var firstName = reader.GetField(1);
        var secondRead = reader.Read();
        var secondName = reader.GetField(1);
        var eofRead = reader.Read();

        // Assert
        Assert.True(firstRead);
        Assert.Equal("Ada", firstName);
        Assert.True(secondRead);
        Assert.Equal("Bob", secondName);
        Assert.False(eofRead);
    }

    [Fact]
    public void CultureAwareParsing_ParsesDecimal()
    {
        // Arrange
        const string csv = "amount;date\n12,5;31/12/2025\n";
        var options = new CsvOptions
        {
            Delimiter = ';',
            CultureInfo = CultureInfo.GetCultureInfo("fr-FR")
        };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<CultureRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal(12.5m, row.Amount);
        Assert.Equal(new DateOnly(2025, 12, 31), row.Date);
    }

    [Fact]
    public void MissingField_ThrowsInStrictMode()
    {
        // Arrange
        const string csv = "id\n1\n";
        var options = new CsvOptions
        {
            ReadMode = CsvReadMode.Strict
        };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();
        Action act = () => reader.GetRecord<RequiredColumnsRecord>();

        // Assert
        Assert.True(read);
        Assert.Throws<CsvException>(act);
    }

    [Fact]
    public void TryReadDynamic_ReturnsExpandoWithHeaderValues()
    {
        // Arrange
        const string csv = "id,name\n1,Ada\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.TryReadDynamic(out var dynamicRow);
        var values = Assert.IsAssignableFrom<IDictionary<string, object?>>((object?)dynamicRow);

        // Assert
        Assert.True(read);
        Assert.Equal("1", values["id"]);
        Assert.Equal("Ada", values["name"]);
    }

    [Fact]
    public async Task ReadDictionaryAsync_WithoutHeader_UsesGeneratedColumnNames()
    {
        // Arrange
        const string csv = "1,Ada\n";
        var options = new CsvOptions { HasHeader = false };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var values = await reader.ReadDictionaryAsync();
        var eofValues = await reader.ReadDictionaryAsync();

        // Assert
        Assert.NotNull(values);
        Assert.Equal("1", values["Column0"]);
        Assert.Equal("Ada", values["Column1"]);
        Assert.Null(eofValues);
    }

    [Fact]
    public void TryReadDictionary_WhenRowHasExtraFields_GeneratesAdditionalColumnNames()
    {
        // Arrange
        const string csv = "id\n1,Ada,Engineer\n";
        var options = new CsvOptions { DetectColumnCount = false };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.TryReadDictionary(out var values);

        // Assert
        Assert.True(read);
        Assert.NotNull(values);
        Assert.Equal("1", values["id"]);
        Assert.Equal("Ada", values["Column1"]);
        Assert.Equal("Engineer", values["Column2"]);
    }

    [Fact]
    public void TryReadDictionary_WhenRowHasMissingFields_AssignsNullValuesForHeaderColumns()
    {
        // Arrange
        const string csv = "id,name,role\n1,Ada\n";
        var options = new CsvOptions { DetectColumnCount = false };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.TryReadDictionary(out var values);

        // Assert
        Assert.True(read);
        Assert.NotNull(values);
        Assert.Equal("1", values["id"]);
        Assert.Equal("Ada", values["name"]);
        Assert.Null(values["role"]);
    }

    [Fact]
    public void DetectedNewLine_UsesFirstObservedNewLineSequence()
    {
        // Arrange
        const string csv = "id,name\r\n1,Ada\r\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.Read();

        // Assert
        Assert.True(read);
        Assert.Equal("\r\n", reader.DetectedNewLine);
    }

    [Fact]
    public void LineNumber_TracksRecordStartLine()
    {
        // Arrange
        const string csv = "id,name\n1,Ada\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.Read();

        // Assert
        Assert.True(read);
        Assert.Equal(2, reader.LineNumber);
    }

    [Fact]
    public void LineNumber_AccountsForEmbeddedNewLinesInsideQuotedFields()
    {
        // Arrange
        const string csv = "id,notes\n1,\"line1\nline2\"\n2,Bob\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var firstRead = reader.Read();
        var firstLine = reader.LineNumber;
        var secondRead = reader.Read();
        var secondLine = reader.LineNumber;

        // Assert
        Assert.True(firstRead);
        Assert.Equal(2, firstLine);
        Assert.True(secondRead);
        Assert.Equal(4, secondLine);
    }

    [Fact]
    public void PrepareHeaderForMatch_AllowsCustomHeaderNormalization()
    {
        // Arrange
        const string csv = " ID , Name \n1,Ada\n";
        var options = new CsvOptions
        {
            PrepareHeaderForMatch = static (header, _) => header.Trim().ToLowerInvariant()
        };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<SimpleRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal(1, row.Id);
        Assert.Equal("Ada", row.Name);
    }

    [Fact]
    public void HeaderValidated_IsInvokedWhenHeaderIsRead()
    {
        // Arrange
        const string csv = "id,name\n1,Ada\n";
        var calls = 0;
        string[]? headers = null;
        var options = new CsvOptions
        {
            HeaderValidated = context =>
            {
                calls++;
                headers = context.Headers.ToArray();
            }
        };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();

        // Assert
        Assert.True(read);
        Assert.Equal(1, calls);
        Assert.NotNull(headers);
        Assert.Equal(["id", "name"], headers);
    }

    [Fact]
    public void MissingFieldFound_IsInvokedWhenMappedColumnIsMissing()
    {
        // Arrange
        const string csv = "id\n1\n";
        var calls = 0;
        CsvMissingFieldContext captured = default;
        var options = new CsvOptions
        {
            ReadMode = CsvReadMode.Lenient,
            MissingFieldFound = context =>
            {
                calls++;
                captured = context;
            }
        };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();
        _ = reader.GetRecord<SimpleRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal(1, calls);
        Assert.Equal("Name", captured.MemberName);
        Assert.Equal(-1, captured.FieldIndex);
    }

    [Fact]
    public void ReadingExceptionOccurred_CanSuppressStrictParserExceptions()
    {
        // Arrange
        const string csv = "id,name\n1,te\"st\n";
        var calls = 0;
        var options = new CsvOptions
        {
            ReadMode = CsvReadMode.Strict,
            ReadingExceptionOccurred = _ =>
            {
                calls++;
                return true;
            }
        };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();
        var name = reader.GetField(1);

        // Assert
        Assert.True(read);
        Assert.Equal("te\"st", name);
        Assert.Equal(1, calls);
    }

    private sealed class CultureRecord
    {
        public decimal Amount { get; set; }

        public DateOnly Date { get; set; }
    }

    private sealed class RequiredColumnsRecord
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class SimpleRecord
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
