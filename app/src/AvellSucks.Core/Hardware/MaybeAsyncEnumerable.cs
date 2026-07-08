using System.Runtime.CompilerServices;

namespace AvellSucks.Core.Hardware;

internal readonly struct MaybeAsyncEnumerable<T>(IEnumerable<T> inner)
{
    private readonly IEnumerable<T> _inner = inner;

    public IAsyncEnumerable<T> AsAsyncEnumerable(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new AsyncEnumerableImpl(_inner);
    }

    private sealed class AsyncEnumerableImpl : IAsyncEnumerable<T>
    {
        private readonly IEnumerable<T> _source;

        public AsyncEnumerableImpl(IEnumerable<T> source) => _source = source;

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new AsyncEnumeratorImpl(_source.GetEnumerator());
    }

    private sealed class AsyncEnumeratorImpl : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;

        public AsyncEnumeratorImpl(IEnumerator<T> inner) => _inner = inner;

        public T Current => _inner.Current;

        public ValueTask DisposeAsync()
        {
            if (_inner is IAsyncDisposable asyncDisposable)
            {
                return asyncDisposable.DisposeAsync();
            }

            _inner.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            if (_inner.MoveNext())
            {
                return ValueTask.FromResult(true);
            }

            return ValueTask.FromResult(false);
        }
    }
}
