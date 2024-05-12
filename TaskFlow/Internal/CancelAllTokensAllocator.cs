namespace System.Threading.Tasks.Flow.Internal
{
    using System.Collections.Generic;
    using System.Linq;

    internal sealed class CancelAllTokensAllocator
    {
        private readonly object _allocatedTokensLock;
        private readonly HashSet<CancellationTokenSource> _allocatedCancellationTokenSources;

        public CancelAllTokensAllocator()
        {
            _allocatedTokensLock = new object();
            _allocatedCancellationTokenSources = new HashSet<CancellationTokenSource>();
        }

        public IDisposable AllocateCancellationToken(out CancellationToken token)
        {
            var cts = new CancellationTokenSource();
            token = cts.Token;

            lock (_allocatedTokensLock)
            {
                _allocatedCancellationTokenSources.Add(cts);
            }

            return Disposable.Create(() =>
            {
                lock (_allocatedTokensLock)
                {
                    cts.Dispose();
                    _allocatedCancellationTokenSources.Remove(cts);
                }
            });
        }

        public void Cancel()
        {
            lock (_allocatedTokensLock)
            {
                var tokenSourcesCopy = _allocatedCancellationTokenSources.ToArray();

                foreach (var tokenSource in tokenSourcesCopy)
                {
                    tokenSource.Cancel();
                }
            }
        }
    }
}