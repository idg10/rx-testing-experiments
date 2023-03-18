using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Reflection;

namespace RxGenericTestSpike.Tests
{
    internal class ObservableQueryToAync
    {
        public static Func<IObservable<TIn>, IObservable<TOut>> Rewrite<TIn, TOut>(
            Expression<Func<IObservable<TIn>, IObservable<TOut>>> expression)
        {
            // Rewrite the method body expression so that any IObservable extension methods are
            // replaced with their IAsyncObservable equivalents. (This is a somewhat blunt
            // instrument but this is just a demonstration of viability.) We need to make sure
            // that any place the input parameter to this lambda shows up, we also replace that
            // with the async equivalent.
            var asyncExtensionThis = Expression.Parameter(
                typeof(IAsyncObservable<>).MakeGenericType(expression.Parameters[0].Type.GenericTypeArguments[0]),
                expression.Parameters[0].Name);
            Dictionary<ParameterExpression, ParameterExpression> paramSubstitutions = new()
            {
                { expression.Parameters[0], asyncExtensionThis }
            };
            SyncToAsyncRewriter rewriter = new(paramSubstitutions);
            Expression asyncExpressionBody = rewriter.Visit(expression.Body);

            // Now wrap that back up as a lambda.
            var asyncExpression = Expression.Lambda<Func<IAsyncObservable<TIn>, IAsyncObservable<TOut>>>(
                asyncExpressionBody,
                asyncExtensionThis);

            // We want to be able to actually run the thing, so now that we've got a version
            // of the expression transformed into the IAsyncObservable<T> space, compile it.
            Func<IAsyncObservable<TIn>, IAsyncObservable<TOut>> asyncMethod = asyncExpression.Compile();

            // Although we now have an IAsyncObservable<T> flavoured version of the original expression,
            // we have to deal with the fact that the TestScheduler is still going to supply us an
            // ordinary IObservable<T> as input, and will expect an IObservable<T> as output. So we
            // need to stick adapters on the input and output. We don't need to do any fancy expression
            // rewriting for that though - we can just use some code...
            IObservable<TOut> RunRewrittenMethod(IObservable<TIn> source)
            {
                // Convert the source IObs into an IAsyncObjs
                IAsyncObservable<TIn> aObsIn = source.ToAsyncObservable();

                // Pass that into the compiled version of the rewritten expression.
                IAsyncObservable<TOut> aObsOut = asyncMethod(aObsIn);

                // Then we want to adapt it back to an IObservable so TestScheduler can work with it
                return new AsyncObservableToOrdinaryObservable<TOut>(aObsOut);
            }

            return RunRewrittenMethod;
        }

        private class SyncToAsyncRewriter : ExpressionVisitor
        {
            private readonly Dictionary<ParameterExpression, ParameterExpression> paramSubstitutions;

            public SyncToAsyncRewriter(Dictionary<ParameterExpression, ParameterExpression> paramSubstitutions)
            {
                this.paramSubstitutions = paramSubstitutions;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (this.paramSubstitutions.TryGetValue(node, out ParameterExpression? replacement))
                {
                    return base.VisitParameter(replacement);
                }

                return base.VisitParameter(node);
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                MethodInfo mi = node.Method;
                if (mi.DeclaringType == typeof(Observable))
                {
                    ParameterInfo[] parameters = mi.GetParameters();
                    if (parameters.Length > 0)
                    {
                        // TODO: should check this is also an extension method.

                        var writtenParams = parameters
                            .Zip(node.Arguments)
                            .Select(piAndArg =>
                            {
                                (ParameterInfo pi, Expression arg) = piAndArg;
                                Type pType = pi.ParameterType;
                                if (pType.IsGenericType && pType.GetGenericTypeDefinition() == typeof(IObservable<>))
                                {
                                    Type asyncPType = typeof(IAsyncObservable<>).MakeGenericType(pType.GenericTypeArguments[0]);
                                    return (asyncPType, this.Visit(arg));
                                }

                                return (pType, this.Visit(arg));
                            });

                        Type[] asyncParamTypes = writtenParams.Select(p => p.Item1).ToArray();
                        Expression[] asyncParamExpressions = writtenParams.Select(p => p.Item2).ToArray();

                        MethodInfo asyncMethod = typeof(AsyncObservable).GetMethod(
                            mi.Name,
                            asyncParamTypes) ?? throw new InvalidOperationException($"Failed to find match for {mi.Name} on AsyncObservable");
                        return Expression.Call(
                            asyncMethod,
                            asyncParamExpressions);
                    }
                }
                return base.VisitMethodCall(node);
            }
        }

        // A thoroughly half-baked adapter to present an IAsyncObservable<T> as an ordinary IObservable<T>.
        // This is all kinds of wrong, and it's only here so we can demonstrate the viability of expression
        // rewriting.
        private class AsyncObservableToOrdinaryObservable<T> : IObservable<T>
        {
            private readonly IAsyncObservable<T> asyncSource;

            public AsyncObservableToOrdinaryObservable(IAsyncObservable<T> asyncSource)
            {
                this.asyncSource = asyncSource;
            }

            public IDisposable Subscribe(IObserver<T> observer)
            {
                ObserverAdapter adapter = new(
                    observer,
                    this.asyncSource);
                return adapter;
            }

            private class ObserverAdapter : IAsyncObserver<T>, IDisposable
            {
                private readonly IObserver<T> observer;
                private readonly IAsyncDisposable disp;

                public ObserverAdapter(IObserver<T> observer, IAsyncObservable<T> asyncSource)
                {
                    this.observer = observer;
                    this.disp =  asyncSource.SubscribeAsync(this).AsTask().Result; // Would it work just to not block?
                }

                public void Dispose()
                {
                    this.disp.DisposeAsync().AsTask().Wait(); // Would it work just to not block?
                }

                public ValueTask OnCompletedAsync()
                {
                    this.observer.OnCompleted();
                    return ValueTask.CompletedTask;
                }

                public ValueTask OnErrorAsync(Exception error)
                {
                    this.observer.OnError(error);
                    return ValueTask.CompletedTask;
                }

                public ValueTask OnNextAsync(T value)
                {
                    this.observer.OnNext(value);
                    return ValueTask.CompletedTask;
                }
            }
        }
    }
}
