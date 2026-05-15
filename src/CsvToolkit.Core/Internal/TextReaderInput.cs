namespace CsvToolkit.Core.Internal;

internal sealed class TextReaderInput(TextReader reader, bool leaveOpen) : ICsvCharInput
{
    public int Read(Span<char> destination)
    {
        return reader.Read(destination);
    }

    public ValueTask<int> ReadAsync(Memory<char> destination, CancellationToken cancellationToken)
    {
        return reader.ReadAsync(destination, cancellationToken);
    }

    public void Dispose()
    {
        if (!leaveOpen)
        {
            reader.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!leaveOpen)
        {
            if (reader is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                return;
            }

            reader.Dispose();
        }
    }
}
