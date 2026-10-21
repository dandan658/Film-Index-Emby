using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    public static class ScheduledWork
    {
        // Work already holding a semaphore permit must run its finally block even after cancellation.
        // The launch loop owns this list; workers must not append to it.
        public static async Task Run(Func<List<Task>, Task> launch, Action completed)
        {
            var pending = new List<Task>();
            try { await launch(pending).ConfigureAwait(false); }
            finally
            {
                try { await Task.WhenAll(pending).ConfigureAwait(false); }
                finally { completed?.Invoke(); }
            }
        }
    }
}
