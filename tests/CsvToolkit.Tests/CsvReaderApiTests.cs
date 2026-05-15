using System.Data;
using System.Globalization;

namespace CsvToolkit.Core.Tests;

public sealed class CsvReaderApiTests
{
    [Fact]
    public void GetField_ByName_ReadsCurrentRowField()
    {
        // Arrange
        const string csv = "id,name\n1,Ada\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.Read();
        var id = reader.GetField<int>("id");
        var name = reader.GetField("name");

        // Assert
        Assert.True(read);
        Assert.Equal(1, id);
        Assert.Equal("Ada", name);
    }

    [Fact]
    public void GetFieldSpan_ByName_ReadsCurrentRowFieldWithoutStringMaterialization()
    {
        // Arrange
        const string csv = "id,name\n1,Ada\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.Read();
        var id = int.Parse(reader.GetFieldSpan("id"), CultureInfo.InvariantCulture);
        var name = reader.GetFieldSpan("name");

        // Assert
        Assert.True(read);
        Assert.Equal(1, id);
        Assert.True(name.SequenceEqual("Ada"));
    }

    [Fact]
    public void GetFieldMemory_ByName_ReadsCurrentRowField()
    {
        // Arrange
        const string csv = "id,name\n1,Ada\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.Read();
        var name = reader.GetFieldMemory("name");

        // Assert
        Assert.True(read);
        Assert.Equal("Ada", name.ToString());
    }

    [Fact]
    public void GetFieldIndex_ResolvesDuplicateHeaderByNameIndex()
    {
        // Arrange
        const string csv = "name,name,age\nAda,Lovelace,36\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var firstNameIndex = reader.GetFieldIndex("name", nameIndex: 0);
        var lastNameIndex = reader.GetFieldIndex("name", nameIndex: 1);
        var read = reader.TryReadRow(out var row);

        // Assert
        Assert.True(read);
        Assert.Equal("Ada", row.GetFieldString(firstNameIndex));
        Assert.Equal("Lovelace", row.GetFieldString(lastNameIndex));
    }

    [Fact]
    public void TryReadRow_WithPreResolvedIndexes_SupportsManualMapping()
    {
        // Arrange
        const string csv = "id,name\n1,Ada\n2,Bob\n";
        using var reader = new CsvReader(new StringReader(csv));
        var idIndex = reader.GetFieldIndex("id");
        var nameIndex = reader.GetFieldIndex("name");
        var records = new List<ApiRecord>();

        // Act
        while (reader.TryReadRow(out var row))
        {
            records.Add(new ApiRecord
            {
                Id = int.Parse(row.GetFieldSpan(idIndex), CultureInfo.InvariantCulture),
                Name = row.GetFieldString(nameIndex)
            });
        }

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal(1, records[0].Id);
        Assert.Equal("Ada", records[0].Name);
        Assert.Equal(2, records[1].Id);
        Assert.Equal("Bob", records[1].Name);
    }

    [Fact]
    public void ReadRows_WithPreResolvedIndexes_SupportsStaticProjection()
    {
        // Arrange
        const string csv = "id,name\n1,Ada\n2,Bob\n";
        using var reader = new CsvReader(new StringReader(csv));
        var state = new ReadRowsState(reader.GetFieldIndex("id"), reader.GetFieldIndex("name"));

        // Act
        var count = reader.ReadRows(state, static (csvReader, readState) =>
        {
            readState.Records.Add(new ApiRecord
            {
                Id = csvReader.GetInt32(readState.IdIndex),
                Name = csvReader.GetField(readState.NameIndex)
            });
        });

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(2, state.Records.Count);
        Assert.Equal(1, state.Records[0].Id);
        Assert.Equal("Ada", state.Records[0].Name);
        Assert.Equal(2, state.Records[1].Id);
        Assert.Equal("Bob", state.Records[1].Name);
    }

    [Fact]
    public void TypedManualAccessors_ReadBuiltInValues()
    {
        // Arrange
        const string csv = "id,amount,created,flag,score,optionalCreated,optionalFlag\n" +
                           "42,123.45,2025-01-02T03:04:05.0000000Z,true,,,\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.Read();

        // Assert
        Assert.True(read);
        Assert.Equal(42, reader.GetInt32("id"));
        Assert.Equal(123.45m, reader.GetDecimal("amount"));
        Assert.Equal(DateTime.Parse("2025-01-02T03:04:05.0000000Z", CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind), reader.GetDateTime("created"));
        Assert.True(reader.GetBoolean("flag"));
        Assert.Null(reader.GetNullableInt32("score"));
        Assert.Null(reader.GetNullableDateTime("optionalCreated"));
        Assert.Null(reader.GetNullableBoolean("optionalFlag"));
    }

    [Fact]
    public void TypedManualAccessors_ThrowInStrictMode_WhenConversionFails()
    {
        // Arrange
        const string csv = "id\nnot-an-int\n";
        using var reader = new CsvReader(new StringReader(csv));
        Assert.True(reader.Read());

        // Act + Assert
        var exception = Assert.Throws<CsvException>(() => reader.GetInt32("id"));
        Assert.Contains("Failed to convert field", exception.Message);
    }

    [Fact]
    public void GetField_GenericByName_UsesConverterOptions()
    {
        // Arrange
        const string csv = "flag\nY\n";
        var options = new CsvOptions();
        options.ConverterOptions.Configure<bool>(o => o.AddTrueValues("Y").AddFalseValues("N"));
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();
        var flag = reader.GetField<bool>("flag");

        // Assert
        Assert.True(read);
        Assert.True(flag);
    }

    [Fact]
    public void GetRecords_EnumeratesAllRecords()
    {
        // Arrange
        const string csv = "Id,Name\n1,Ada\n2,Bob\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var records = reader.GetRecords<ApiRecord>().ToList();

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal("Ada", records[0].Name);
        Assert.Equal("Bob", records[1].Name);
    }

    [Fact]
    public async Task GetRecordsAsync_EnumeratesAllRecords()
    {
        // Arrange
        const string csv = "Id,Name\n1,Ada\n2,Bob\n";
        await using var reader = new CsvReader(new StringReader(csv));

        // Act
        var records = new List<ApiRecord>();
        await foreach (var record in reader.GetRecordsAsync<ApiRecord>())
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal(1, records[0].Id);
        Assert.Equal(2, records[1].Id);
    }

    [Fact]
    public void CsvDataReader_LoadsIntoDataTable()
    {
        // Arrange
        const string csv = "id,name\n1,Ada\n2,Bob\n";
        using var reader = new CsvReader(new StringReader(csv));
        using var dataReader = reader.AsDataReader();
        var table = new DataTable();

        // Act
        table.Load(dataReader);

        // Assert
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Columns.Count);
        Assert.Equal("id", table.Columns[0].ColumnName);
        Assert.Equal("Ada", table.Rows[0][1]);
        Assert.Equal("Bob", table.Rows[1][1]);
    }

    private sealed class ApiRecord
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class ReadRowsState(int idIndex, int nameIndex)
    {
        public int IdIndex { get; } = idIndex;

        public int NameIndex { get; } = nameIndex;

        public List<ApiRecord> Records { get; } = [];
    }
}
