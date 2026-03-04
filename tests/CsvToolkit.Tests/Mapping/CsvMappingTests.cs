using System.Globalization;
using CsvToolkit.Core.Mapping;
using CsvToolkit.Core.TypeConversion;

namespace CsvToolkit.Core.Tests.Mapping;

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

    [Fact]
    public void NoHeaderMapping_MixedIndexedAndImplicitMembers_UseDistinctOrdinals()
    {
        // Arrange
        const string csv = "11,22\n";
        var options = new CsvOptions
        {
            HasHeader = false,
            CultureInfo = CultureInfo.InvariantCulture
        };
        var maps = new CsvMapRegistry();
        maps.Register<NoHeaderMixedIndexRecord>(map =>
        {
            map.Map(x => x.Second).Index(0);
        });
        using var reader = new CsvReader(new StringReader(csv), options, maps);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<NoHeaderMixedIndexRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal(22, row.First);
        Assert.Equal(11, row.Second);
    }

    [Fact]
    public void FluentMapping_DuplicateHeaderName_UsesNameIndex()
    {
        // Arrange
        const string csv = "name,name,age\nAda,Lovelace,36\n";
        var maps = new CsvMapRegistry();
        maps.Register<DuplicateNameRecord>(map =>
        {
            map.Map(x => x.FirstName).Name("name").NameIndex(0);
            map.Map(x => x.LastName).Name("name").NameIndex(1);
            map.Map(x => x.Age).Name("age");
        });
        using var reader = new CsvReader(new StringReader(csv), mapRegistry: maps);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<DuplicateNameRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal("Ada", row.FirstName);
        Assert.Equal("Lovelace", row.LastName);
        Assert.Equal(36, row.Age);
    }

    [Fact]
    public void FluentMapping_OptionalMember_MissingColumn_DoesNotThrow()
    {
        // Arrange
        const string csv = "id\n1\n";
        var maps = new CsvMapRegistry();
        maps.Register<OptionalRecord>(map =>
        {
            map.Map(x => x.Missing).Name("missing").Optional();
        });
        using var reader = new CsvReader(new StringReader(csv), mapRegistry: maps);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<OptionalRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal(1, row.Id);
        Assert.Equal(0, row.Missing);
    }

    [Fact]
    public void FluentMapping_DefaultValue_UsedWhenConversionFails()
    {
        // Arrange
        const string csv = "id,score\n1,\n";
        var maps = new CsvMapRegistry();
        maps.Register<DefaultValueRecord>(map =>
        {
            map.Map(x => x.Score).Name("score").Default(99);
        });
        using var reader = new CsvReader(new StringReader(csv), mapRegistry: maps);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<DefaultValueRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal(1, row.Id);
        Assert.Equal(99, row.Score);
    }

    [Fact]
    public void FluentMapping_ConstantValue_OverridesInput()
    {
        // Arrange
        const string csv = "id,country\n1,FR\n";
        var maps = new CsvMapRegistry();
        maps.Register<ConstantValueRecord>(map =>
        {
            map.Map(x => x.Country).Name("country").Constant("US");
        });
        using var reader = new CsvReader(new StringReader(csv), mapRegistry: maps);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<ConstantValueRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal(1, row.Id);
        Assert.Equal("US", row.Country);
    }

    [Fact]
    public void FluentMapping_Validate_ThrowsInStrictMode()
    {
        // Arrange
        const string csv = "id,age\n1,15\n";
        var maps = new CsvMapRegistry();
        maps.Register<ValidationRecord>(map =>
        {
            map.Map(x => x.Age).Name("age").Validate(age => age >= 18, "Age must be at least 18.");
        });
        using var reader = new CsvReader(new StringReader(csv), mapRegistry: maps);

        // Act
        var read = reader.Read();
        Action act = () => reader.GetRecord<ValidationRecord>();

        // Assert
        Assert.True(read);
        var exception = Assert.Throws<CsvException>(act);
        Assert.Equal("Age must be at least 18.", exception.Message);
    }

    [Fact]
    public void ConstructorMapping_ReadsTypeWithoutParameterlessConstructor()
    {
        // Arrange
        const string csv = "id,name,age\n1,Ada,36\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<ImmutableCtorRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal(1, row.Id);
        Assert.Equal("Ada", row.Name);
        Assert.Equal(36, row.Age);
    }

    [Fact]
    public void GlobalConverterOptions_NullAndBooleanValues_AreApplied()
    {
        // Arrange
        const string csv = "id,flag,score\n1,Y,NULL\n";
        var options = new CsvOptions();
        options.ConverterOptions.Configure<bool>(o => o.AddTrueValues("Y").AddFalseValues("N"));
        options.ConverterOptions.Configure<int?>(o => o.AddNullValues("NULL"));
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<ConverterOptionsRecord>();

        // Assert
        Assert.True(read);
        Assert.True(row.Flag);
        Assert.Null(row.Score);
    }

    [Fact]
    public void GlobalConverterOptions_NumberStyles_AreApplied()
    {
        // Arrange
        const string csv = "value\nFF\n";
        var options = new CsvOptions();
        options.ConverterOptions.Configure<int>(o => o.NumberStyles = NumberStyles.HexNumber);
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<HexRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal(255, row.Value);
    }

    [Fact]
    public void GlobalConverterOptions_DateFormats_AreApplied()
    {
        // Arrange
        const string csv = "created\n31-12-2025\n";
        var options = new CsvOptions { CultureInfo = CultureInfo.InvariantCulture };
        options.ConverterOptions.Configure<DateTime>(o =>
        {
            o.AddFormats("dd-MM-yyyy");
            o.DateTimeStyles = DateTimeStyles.None;
        });
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<DateRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal(new DateTime(2025, 12, 31), row.Created);
    }

    [Fact]
    public void MemberConverterOptions_OverrideGlobalOptions()
    {
        // Arrange
        const string csv = "flag\nT\n";
        var options = new CsvOptions();
        options.ConverterOptions.Configure<bool>(o => o.AddTrueValues("Y").AddFalseValues("N"));
        var maps = new CsvMapRegistry();
        maps.Register<OverrideBoolRecord>(map =>
        {
            map.Map(x => x.Flag).TrueValues("T").FalseValues("F");
        });
        using var reader = new CsvReader(new StringReader(csv), options, maps);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<OverrideBoolRecord>();

        // Assert
        Assert.True(read);
        Assert.True(row.Flag);
    }

    [Fact]
    public void AttributeMapping_NameIndexAndOptional_AreApplied()
    {
        // Arrange
        const string csv = "name,name\nAda,Lovelace\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<AttributeNameIndexOptionalRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal("Ada", row.FirstName);
        Assert.Equal("Lovelace", row.LastName);
        Assert.Equal(0, row.Missing);
    }

    [Fact]
    public void AttributeMapping_DefaultAndConstant_AreApplied()
    {
        // Arrange
        const string csv = "id,score,country\n1,,FR\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<AttributeDefaultConstantRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal(1, row.Id);
        Assert.Equal(99, row.Score);
        Assert.Equal("US", row.Country);
    }

    [Fact]
    public void AttributeMapping_Validate_ThrowsInStrictMode()
    {
        // Arrange
        const string csv = "age\n15\n";
        using var reader = new CsvReader(new StringReader(csv));

        // Act
        var read = reader.Read();
        Action act = () => reader.GetRecord<AttributeValidationRecord>();

        // Assert
        Assert.True(read);
        var exception = Assert.Throws<CsvException>(act);
        Assert.Equal("Age must be at least 18.", exception.Message);
    }

    [Fact]
    public void AttributeConverterOptions_OverrideGlobalOptions()
    {
        // Arrange
        const string csv = "flag,created,amount,score\nT,31-12-2025,FF,NULL\n";
        var options = new CsvOptions { CultureInfo = CultureInfo.InvariantCulture };
        options.ConverterOptions.Configure<bool>(o => o.AddTrueValues("Y").AddFalseValues("N"));
        options.ConverterOptions.Configure<DateTime>(o => o.AddFormats("yyyyMMdd"));
        options.ConverterOptions.Configure<int>(o => o.NumberStyles = NumberStyles.Integer);
        options.ConverterOptions.Configure<int?>(o => o.AddNullValues("EMPTY"));
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<AttributeConverterOptionsRecord>();

        // Assert
        Assert.True(read);
        Assert.True(row.Flag);
        Assert.Equal(new DateTime(2025, 12, 31), row.Created);
        Assert.Equal(255, row.Amount);
        Assert.Null(row.Score);
    }

    [Fact]
    public void AttributeConverterOptions_Culture_IsApplied()
    {
        // Arrange
        const string csv = "price\n1,5\n";
        var options = new CsvOptions
        {
            Delimiter = ';',
            CultureInfo = CultureInfo.InvariantCulture
        };
        using var reader = new CsvReader(new StringReader(csv), options);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<AttributeCultureRecord>();

        // Assert
        Assert.True(read);
        Assert.Equal(1.5m, row.Price);
    }

    [Fact]
    public void FluentConverterOptions_OverrideAttributeConverterOptions()
    {
        // Arrange
        const string csv = "flag\nY\n";
        var maps = new CsvMapRegistry();
        maps.Register<AttributeFluentOverrideRecord>(map =>
        {
            map.Map(x => x.Flag).TrueValues("Y").FalseValues("N");
        });
        using var reader = new CsvReader(new StringReader(csv), mapRegistry: maps);

        // Act
        var read = reader.Read();
        var row = reader.GetRecord<AttributeFluentOverrideRecord>();

        // Assert
        Assert.True(read);
        Assert.True(row.Flag);
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

    private sealed class NoHeaderMixedIndexRecord
    {
        public int First { get; set; }

        public int Second { get; set; }
    }

    private sealed class DuplicateNameRecord
    {
        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public int Age { get; set; }
    }

    private sealed class OptionalRecord
    {
        public int Id { get; set; }

        public int Missing { get; set; }
    }

    private sealed class DefaultValueRecord
    {
        public int Id { get; set; }

        public int Score { get; set; }
    }

    private sealed class ConstantValueRecord
    {
        public int Id { get; set; }

        public string Country { get; set; } = string.Empty;
    }

    private sealed class ValidationRecord
    {
        public int Id { get; set; }

        public int Age { get; set; }
    }

    private sealed class ImmutableCtorRecord
    {
        public ImmutableCtorRecord(int id, string name, int age)
        {
            Id = id;
            Name = name;
            Age = age;
        }

        public int Id { get; }

        public string Name { get; }

        public int Age { get; }
    }

    private sealed class ConverterOptionsRecord
    {
        public int Id { get; set; }

        public bool Flag { get; set; }

        public int? Score { get; set; }
    }

    private sealed class HexRecord
    {
        public int Value { get; set; }
    }

    private sealed class DateRecord
    {
        public DateTime Created { get; set; }
    }

    private sealed class OverrideBoolRecord
    {
        public bool Flag { get; set; }
    }

    private sealed class AttributeNameIndexOptionalRecord
    {
        [CsvColumn("name"), CsvNameIndex(0)] public string FirstName { get; set; } = string.Empty;

        [CsvColumn("name"), CsvNameIndex(1)] public string LastName { get; set; } = string.Empty;

        [CsvColumn("missing"), CsvOptional] public int Missing { get; set; }
    }

    private sealed class AttributeDefaultConstantRecord
    {
        public int Id { get; set; }

        [CsvDefault(99)] public int Score { get; set; }

        [CsvConstant("US")] public string Country { get; set; } = string.Empty;
    }

    private sealed class AttributeValidationRecord
    {
        [CsvValidate(nameof(IsAdult), Message = "Age must be at least 18.")]
        public int Age { get; set; }

        private static bool IsAdult(int age)
        {
            return age >= 18;
        }
    }

    private sealed class AttributeConverterOptionsRecord
    {
        [CsvTrueValues("T"), CsvFalseValues("F")]
        public bool Flag { get; set; }

        [CsvFormats("dd-MM-yyyy")]
        public DateTime Created { get; set; }

        [CsvNumberStyles(NumberStyles.HexNumber)]
        public int Amount { get; set; }

        [CsvNullValues("NULL")]
        public int? Score { get; set; }
    }

    private sealed class AttributeCultureRecord
    {
        [CsvCulture("fr-FR")]
        public decimal Price { get; set; }
    }

    private sealed class AttributeFluentOverrideRecord
    {
        [CsvTrueValues("T"), CsvFalseValues("F")]
        public bool Flag { get; set; }
    }
}
