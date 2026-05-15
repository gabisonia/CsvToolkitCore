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
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask(writer.FlushAsync());
    }

    public void Dispose()
    {
        if (!leaveOpen)
        {
            writer.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!leaveOpen)
        {
            if (writer is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                return;
            }

            writer.Dispose();
        }
    }
}
