using System.Globalization;
using CsvToolkit.Mapping;
using CsvToolkit.TypeConversion;

namespace CsvToolkit.Tests.Mapping;

public sealed class CsvMappingTests
{
    [Fact]
    public void AttributeMapping_ReadsStronglyTypedRecord()
    {
        // Arrange
        const string csv = "identifier,full_name,status,score,created,ignored\n1,Ada,Active,42,2025-01-02,skip\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.Read();
        var person = reader.GetRecord<AttributedPerson>();

        // Assert
        Assert.True(read);
        Assert.Equal(1, person.Id);
        Assert.Equal("Ada", person.Name);
        Assert.Equal(PersonStatus.Active, person.Status);
        Assert.Equal(42, person.Score);
        Assert.Equal(new DateOnly(2025, 1, 2), person.Created);
        Assert.Null(person.Ignored);
    }

    [Fact]
    public void FluentMapping_ReadsByConfiguredNamesAndIndexes()
    {
        // Arrange
        const string csv = "first,last,years\nAda,Lovelace,36\n";
        var maps = new CsvMapRegistry();
        maps.Register<FluentPerson>(map =>
        {
            map.Map(x => x.FirstName).Name("first").Index(0);
            map.Map(x => x.LastName).Name("last").Index(1);
            map.Map(x => x.Age).Name("years").Index(2);
        });
        using var reader = new CsvReader(new StringReader(csv), mapRegistry: maps);

        // Act
        var read = reader.Read();
        var person = reader.GetRecord<FluentPerson>();

        // Assert
        Assert.True(read);
        Assert.Equal("Ada", person.FirstName);
        Assert.Equal("Lovelace", person.LastName);
        Assert.Equal(36, person.Age);
    }

    [Fact]
    public void FluentMapping_UsesCustomConverter()
    {
        // Arrange
        const string csv = "name\nada\n";
        var maps = new CsvMapRegistry();
        maps.Register<CustomNameRecord>(map =>
        {
            map.Map(x => x.Name).Name("name").Converter(new UpperCaseStringConverter());
        });
        using var reader = new CsvReader(new StringReader(csv), mapRegistry: maps);

        // Act
        var read = reader.Read();
        var record = reader.GetRecord<CustomNameRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal("ADA", record.Name);
    }

    [Fact]
    public void NullableAndEnumConversion_AreSupported()
    {
        // Arrange
        const string csv = "id,status,score\n1,Inactive,\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<NullableRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal(1, row.Id);
        Assert.Equal(PersonStatus.Inactive, row.Status);
        Assert.Null(row.Score);
    }

    [Fact]
    public void NumericBooleanConversion_AreSupported()
    {
        // Arrange
        const string csv = "flag\n1\n0\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var firstRead = reader.Read();
        var first = reader.GetRecord<BooleanRecord>();
        var secondRead = reader.Read();
        var second = reader.GetRecord<BooleanRecord>();

        // Assert
        Assert.True(firstRead);
        Assert.True(first.Flag);
        Assert.True(secondRead);
        Assert.False(second.Flag);
    }

    [Fact]
    public void BuiltInTypeConversions_WithSemicolonDelimiter_AreSupported()
    {
        // Arrange
        const string csv =
            "byteValue;sbyteValue;shortValue;ushortValue;intValue;uintValue;longValue;ulongValue;floatValue;doubleValue;decimalValue;charValue;boolValue;guidValue;dateTimeValue;dateOnlyValue;timeOnlyValue;nullableIntValue\n" +
            "7;-8;-9;10;11;12;13;14;1.5;2.25;3.75;Z;true;aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee;2025-01-02 03:04:05;2025-01-02;03:04:05;\n";
        var options = new CsvOptions
        {
            Delimiter = ';',
            CultureInfo = CultureInfo.InvariantCulture
        };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<AllTypesRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal((byte)7, row.ByteValue);
        Assert.Equal((sbyte)-8, row.SByteValue);
        Assert.Equal((short)-9, row.ShortValue);
        Assert.Equal((ushort)10, row.UShortValue);
        Assert.Equal(11, row.IntValue);
        Assert.Equal((uint)12, row.UIntValue);
        Assert.Equal(13L, row.LongValue);
        Assert.Equal(14UL, row.ULongValue);
        Assert.Equal(1.5f, row.FloatValue);
        Assert.Equal(2.25d, row.DoubleValue);
        Assert.Equal(3.75m, row.DecimalValue);
        Assert.Equal('Z', row.CharValue);
        Assert.True(row.BoolValue);
        Assert.Equal(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), row.GuidValue);
        Assert.Equal(new DateTime(2025, 1, 2, 3, 4, 5), row.DateTimeValue);
        Assert.Equal(new DateOnly(2025, 1, 2), row.DateOnlyValue);
        Assert.Equal(new TimeOnly(3, 4, 5), row.TimeOnlyValue);
        Assert.Null(row.NullableIntValue);
    }

    [Fact]
    public void NoHeaderMapping_WithPipeDelimiter_ReadsByOrdinal()
    {
        // Arrange
        const string csv = "1|Ada|42.5\n";
        var options = new CsvOptions
        {
            HasHeader = false,
            Delimiter = '|',
            CultureInfo = CultureInfo.InvariantCulture
        };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<NoHeaderPipeRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal(1, row.Id);
        Assert.Equal("Ada", row.Name);
        Assert.Equal(42.5d, row.Score);
    }

    private sealed class UpperCaseStringConverter : ICsvTypeConverter<string>
    {
        public bool TryParse(ReadOnlySpan<char> source, in CsvConverterContext context, out string value)
        {
            value = source.ToString().ToUpperInvariant();
            return true;
        }

        public string Format(string value, in CsvConverterContext context)
        {
            return value.ToLowerInvariant();
        }
    }

    private enum PersonStatus
    {
        Active,
        Inactive
    }

    private sealed class AttributedPerson
    {
        [CsvColumn("identifier")] public int Id { get; set; }

        [CsvColumn("full_name")] public string Name { get; set; } = string.Empty;

        [CsvColumn("status")] public PersonStatus Status { get; set; }

        [CsvColumn("score")] public int? Score { get; set; }

        [CsvColumn("created")] public DateOnly Created { get; set; }

        [CsvIgnore] public string? Ignored { get; set; }
    }

    private sealed class FluentPerson
    {
        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public int Age { get; set; }
    }

    private sealed class CustomNameRecord
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class NullableRecord
    {
        public int Id { get; set; }

        public PersonStatus Status { get; set; }

        public int? Score { get; set; }
    }

    private sealed class BooleanRecord
    {
        public bool Flag { get; set; }
    }

    private sealed class AllTypesRecord
    {
        public byte ByteValue { get; set; }

        public sbyte SByteValue { get; set; }

        public short ShortValue { get; set; }

        public ushort UShortValue { get; set; }

        public int IntValue { get; set; }

        public uint UIntValue { get; set; }

        public long LongValue { get; set; }

        public ulong ULongValue { get; set; }

        public float FloatValue { get; set; }

        public double DoubleValue { get; set; }

        public decimal DecimalValue { get; set; }

        public char CharValue { get; set; }

        public bool BoolValue { get; set; }

        public Guid GuidValue { get; set; }

        public DateTime DateTimeValue { get; set; }

        public DateOnly DateOnlyValue { get; set; }

        public TimeOnly TimeOnlyValue { get; set; }

        public int? NullableIntValue { get; set; }
    }

    private sealed class NoHeaderPipeRecord
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public double Score { get; set; }
    }
}
