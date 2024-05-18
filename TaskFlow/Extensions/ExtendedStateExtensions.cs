namespace System.Threading.Tasks.Flow
{
    using System.Collections.Generic;

    internal static class ExtendedStateExtensions
    {
        public static IEnumerable<T> Unwrap<T>(this ExtendedState? extendedState)
        {
            while (true)
            {
                if (extendedState == null)
                {
                    yield break;
                }

                if (extendedState.State is ExtendedState nestedExtendedState)
                {
                    extendedState = nestedExtendedState;
                    continue;
                }

                if (extendedState.Extended is T state)
                {
                    yield return state;
                }

                break;
            }
        }
    }
}