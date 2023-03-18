using System.Threading.Tasks;
using System.Reactive.Linq;
using System.Linq.Expressions;
using Microsoft.Reactive.Testing;

namespace RxGenericTestSpike.SharedTests
{
    public class AverageTests : ReactiveTest
    {
        public static void Average_Int32_Some(
            Func<Expression<Func<IObservable<int>, IObservable<double>>>, Func<IObservable<int>, IObservable<double>>> queryRewriter)
        {
            var scheduler = new TestScheduler();
            var xs = scheduler.CreateHotObservable(
                OnNext(150, 1),
                OnNext(210, 3),
                OnNext(220, 4),
                OnNext(230, 2),
                OnCompleted<int>(250)
            );

            var res = scheduler.Start(() =>
                queryRewriter(xs =>
                    xs.Average()
                    )(xs)
            );

            res.Messages.AssertEqual(
                OnNext(250, 3.0),
                OnCompleted<double>(250)
            );

            xs.Subscriptions.AssertEqual(
                Subscribe(200, 250)
            );
        }
    }
}