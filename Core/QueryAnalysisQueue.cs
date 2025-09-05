using EFCore.QueryAnalyzer.Core.Models;
using System.Collections.Concurrent;

namespace EFCore.QueryAnalyzer.Core
{
    /// <summary>
    /// Built-in in-memory queue for processing query analysis asynchronously
    /// </summary>
    public sealed class QueryAnalysisQueue
    {
        private readonly ConcurrentQueue<QueryTrackingContext> _queue = new();
        
        /// <summary>
        /// Enqueues a query tracking context for background processing
        /// </summary>
        public void Enqueue(QueryTrackingContext context)
        {
            _queue.Enqueue(context);
        }
        
        /// <summary>
        /// Attempts to dequeue a query tracking context for processing
        /// </summary>
        public bool TryDequeue(out QueryTrackingContext? context)
        {
            return _queue.TryDequeue(out context);
        }
        
        /// <summary>
        /// Gets the current number of items in the queue
        /// </summary>
        public int Count => _queue.Count;
    }
}