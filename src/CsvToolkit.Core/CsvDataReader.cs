using System.Data;
using System.Globalization;

namespace CsvToolkit.Core;

public sealed class CsvDataReader : IDataReader
{
    private readonly CsvReader _reader;
    private readonly bool _leaveOpen;
    private bool _initialized;
    private bool _isClosed;
    private bool _hasBufferedRow;
    private int _fieldCount;
    private string[] _fieldNames = [];

    public CsvDataReader(CsvReader reader, bool leaveOpen = false)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _leaveOpen = leaveOpen;
    }

    public object this[int i] => GetValue(i);

    public object this[string name] => GetValue(GetOrdinal(name));

    public int Depth => 0;

    public bool IsClosed => _isClosed;

    public int RecordsAffected => -1;

    public int FieldCount
    {
        get
        {
            EnsureInitialized();
            return _fieldCount;
        }
    }

    public void Close()
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;
        if (!_leaveOpen)
        {
            _reader.Dispose();
        }
    }

    public void Dispose()
    {
        Close();
    }

    public bool GetBoolean(int i) => Convert.ToBoolean(GetValue(i), CultureInfo.InvariantCulture);

    public byte GetByte(int i) => Convert.ToByte(GetValue(i), CultureInfo.InvariantCulture);

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length)
    {
        var chars = GetString(i).AsSpan();
        if (fieldOffset >= chars.Length)
        {
            return 0;
        }

        var available = chars[(int)fieldOffset..];
        var bytes = System.Text.Encoding.UTF8.GetBytes(available.ToArray());
        var toCopy = Math.Min(length, bytes.Length);
        if (buffer is not null)
        {
            Array.Copy(bytes, 0, buffer, bufferoffset, toCopy);
        }

        return toCopy;
    }

    public char GetChar(int i)
    {
        var value = GetString(i);
        if (value.Length != 1)
        {
            throw new InvalidCastException($"Field {i} cannot be converted to char.");
        }

        return value[0];
    }

    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length)
    {
        var source = GetString(i).AsSpan();
        if (fieldoffset >= source.Length)
        {
            return 0;
        }

        var remaining = source[(int)fieldoffset..];
        var toCopy = Math.Min(length, remaining.Length);
        if (buffer is not null)
        {
            remaining[..toCopy].CopyTo(buffer.AsSpan(bufferoffset, toCopy));
        }

        return toCopy;
    }

    public IDataReader GetData(int i)
    {
        throw new NotSupportedException();
    }

    public string GetDataTypeName(int i)
    {
        _ = i;
        return "string";
    }

    public DateTime GetDateTime(int i) => Convert.ToDateTime(GetValue(i), CultureInfo.InvariantCulture);

    public decimal GetDecimal(int i) => Convert.ToDecimal(GetValue(i), CultureInfo.InvariantCulture);

    public double GetDouble(int i) => Convert.ToDouble(GetValue(i), CultureInfo.InvariantCulture);

    public Type GetFieldType(int i)
    {
        _ = i;
        return typeof(string);
    }

    public float GetFloat(int i) => Convert.ToSingle(GetValue(i), CultureInfo.InvariantCulture);

    public Guid GetGuid(int i)
    {
        var value = GetString(i);
        return Guid.Parse(value);
    }

    public short GetInt16(int i) => Convert.ToInt16(GetValue(i), CultureInfo.InvariantCulture);

    public int GetInt32(int i) => Convert.ToInt32(GetValue(i), CultureInfo.InvariantCulture);

    public long GetInt64(int i) => Convert.ToInt64(GetValue(i), CultureInfo.InvariantCulture);

    public string GetName(int i)
    {
        EnsureInitialized();
        if (i < 0 || i >= _fieldNames.Length)
        {
            throw new IndexOutOfRangeException($"Invalid field index {i}.");
        }

        return _fieldNames[i];
    }

    public int GetOrdinal(string name)
    {
        EnsureInitialized();
        for (var i = 0; i < _fieldNames.Length; i++)
        {
            if (_reader.Options.HeaderComparer.Equals(_fieldNames[i], name))
            {
                return i;
            }
        }

        throw new IndexOutOfRangeException($"Column '{name}' was not found.");
    }

    public DataTable? GetSchemaTable()
    {
        EnsureInitialized();

        var schema = new DataTable("SchemaTable");
        schema.Columns.Add("ColumnName", typeof(string));
        schema.Columns.Add("ColumnOrdinal", typeof(int));
        schema.Columns.Add("ColumnSize", typeof(int));
        schema.Columns.Add("NumericPrecision", typeof(short));
        schema.Columns.Add("NumericScale", typeof(short));
        schema.Columns.Add("DataType", typeof(Type));
        schema.Columns.Add("ProviderType", typeof(int));
        schema.Columns.Add("IsLong", typeof(bool));
        schema.Columns.Add("AllowDBNull", typeof(bool));
        schema.Columns.Add("IsReadOnly", typeof(bool));
        schema.Columns.Add("IsRowVersion", typeof(bool));
        schema.Columns.Add("IsUnique", typeof(bool));
        schema.Columns.Add("IsKey", typeof(bool));
        schema.Columns.Add("IsAutoIncrement", typeof(bool));
        schema.Columns.Add("BaseSchemaName", typeof(string));
        schema.Columns.Add("BaseCatalogName", typeof(string));
        schema.Columns.Add("BaseTableName", typeof(string));
        schema.Columns.Add("BaseColumnName", typeof(string));

        for (var i = 0; i < _fieldCount; i++)
        {
            var row = schema.NewRow();
            row["ColumnName"] = _fieldNames[i];
            row["ColumnOrdinal"] = i;
            row["ColumnSize"] = -1;
            row["NumericPrecision"] = (short)0;
            row["NumericScale"] = (short)0;
            row["DataType"] = typeof(string);
            row["ProviderType"] = DbType.String;
            row["IsLong"] = false;
            row["AllowDBNull"] = true;
            row["IsReadOnly"] = false;
            row["IsRowVersion"] = false;
            row["IsUnique"] = false;
            row["IsKey"] = false;
            row["IsAutoIncrement"] = false;
            row["BaseSchemaName"] = DBNull.Value;
            row["BaseCatalogName"] = DBNull.Value;
            row["BaseTableName"] = DBNull.Value;
            row["BaseColumnName"] = _fieldNames[i];
            schema.Rows.Add(row);
        }

        return schema;
    }

    public string GetString(int i)
    {
        var value = GetValue(i);
        return value is DBNull ? string.Empty : (string)value;
    }

    public object GetValue(int i)
    {
        EnsureInitialized();

        if (i < 0 || i >= _fieldCount)
        {
            throw new IndexOutOfRangeException($"Invalid field index {i}.");
        }

        if (_reader.FieldCount <= i)
        {
            return DBNull.Value;
        }

        return _reader.GetField(i);
    }

    public int GetValues(object[] values)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    public bool IsDBNull(int i)
    {
        return GetValue(i) is DBNull;
    }

    public bool NextResult()
    {
        return false;
    }

    public bool Read()
    {
        EnsureInitialized();

        if (_hasBufferedRow)
        {
            _hasBufferedRow = false;
            return true;
        }

        return _reader.Read();
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        if (_reader.Read())
        {
            _hasBufferedRow = true;
            _fieldCount = _reader.FieldCount;
        }
        else
        {
            _fieldCount = _reader.Headers.Count;
        }

        _fieldNames = BuildFieldNames(_fieldCount, _reader.Headers);
        _initialized = true;
    }

    private static string[] BuildFieldNames(int fieldCount, IReadOnlyList<string> headers)
    {
        if (fieldCount == 0)
        {
            return [];
        }

        var names = new string[fieldCount];
        for (var i = 0; i < fieldCount; i++)
        {
            if (i < headers.Count && !string.IsNullOrWhiteSpace(headers[i]))
            {
                names[i] = headers[i];
                continue;
            }

            names[i] = $"Column{i}";
        }

        return names;
    }
}
