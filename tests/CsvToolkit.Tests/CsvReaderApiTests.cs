using System.Data;

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
}
