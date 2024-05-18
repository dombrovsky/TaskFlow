namespace System.Threading.Tasks.Flow.Internal
{
    using System.Threading.Tasks.Flow.Annotations;

    internal static class Disposable
    {
        public static IDisposable Create(Action action)
        {
            return new ActionDisposable(action);
        }

        private sealed class ActionDisposable : IDisposable
        {
            private readonly Action _action;

            private int _isDisposed;

            public ActionDisposable(Action action)
            {
                Argument.NotNull(action);

                _action = action;
            }

            public void Dispose()
            {
                if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) == 0)
                {
                    _action();
                }
            }
        }
    }
}