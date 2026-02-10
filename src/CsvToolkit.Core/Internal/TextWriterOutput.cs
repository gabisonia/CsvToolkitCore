namespace CsvToolkit.Core.Internal;

internal sealed class TextWriterOutput(TextWriter writer, bool leaveOpen) : ICsvCharOutput
{
    public void Write(ReadOnlySpan<char> source)
    {
        writer.Write(source);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<char> source, CancellationToken cancellationToken)
    {
        return new ValueTask(writer.WriteAsync(source, cancellationToken));
    }

    public void Flush()
    {
        writer.Flush();
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        return new ValueTask(writer.FlushAsync(cancellationToken));
    }

    public void Dispose()
    {
        if (!leaveOpen)
        {
            writer.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!leaveOpen)
        {
            writer.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
