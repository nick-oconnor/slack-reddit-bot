namespace SlackRedditBot.Web.Models
{
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

    public class ObservableQueue<T>
    {
        private readonly ConcurrentQueue<T> queue = new ConcurrentQueue<T>();
        private readonly SemaphoreSlim sem = new SemaphoreSlim(0);

        public void Enqueue(T item)
        {
            this.queue.Enqueue(item);
            this.sem.Release();
        }

        public async Task<T> Dequeue(CancellationToken cancellationToken)
        {
            await this.sem.WaitAsync(cancellationToken);
            this.queue.TryDequeue(out var item);

            return item;
        }
    }
}
