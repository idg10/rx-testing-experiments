using System.Linq.Expressions;

namespace RxGenericTestSpike.Tests
{
    internal class ObservableQueryToSync
    {
        public static Func<IObservable<TIn>, IObservable<TOut>> Rewrite<TIn, TOut>(
            Expression<Func<IObservable<TIn>, IObservable<TOut>>> expression)
        {
            // No rewriting required in this case, because the input is in
            // the IObservable<T> space, and that's exactly the space we want
            // the function we return to execute. So we just compile the
            // incoming expression to make it runnable.
            return expression.Compile();
        }
    }
}
