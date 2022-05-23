using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace GrpcTestService
{
    /// <summary>
    /// An abstract leak-tracking pool that lets you optionally configure detection and tracing.
    /// </summary>
    /// <typeparam name="T">Type of the object to pool</typeparam>
    public abstract class LeakTrackingObjectPool<T> where T : class
    {
        /// <summary>
        /// Whether to detect if an object leaks.
        /// </summary>
        public bool DetectLeaks { get; set; }

        /// <summary>
        /// Trace detailed information on leaks (this should only be used for debugging)
        /// </summary>
        public bool TraceLeaks { get; set; }

        private static readonly ConditionalWeakTable<T, LeakTracker> LeakTrackers = new ConditionalWeakTable<T, LeakTracker>();

        protected void TrackObject(T inst)
        {
            if (this.DetectLeaks)
            {
                var tracker = new LeakTracker();
                LeakTrackers.Add(inst, tracker);
                if (this.TraceLeaks)
                {
                    tracker.Trace = Environment.StackTrace;
                }
            }
        }

        /// <summary>
        /// This method removes an object from tracking.
        /// 
        /// It is only needed for error-checking. ConditionalWeakTable uses
        /// WeakReference to get rid of its own items under normal circumstances. 
        /// </summary>
        /// <param name="trackedObject"></param>
        protected void ForgetTrackedObject(T trackedObject)
        {
            if (this.DetectLeaks)
            {
                if (LeakTrackers.TryGetValue(trackedObject, out LeakTracker tracker))
                {
                    tracker.Dispose();
                    LeakTrackers.Remove(trackedObject);
                }
                else
                {
                    var trace = this.TraceLeaks ? Environment.StackTrace : string.Empty;
                }
            }
        }

        [Conditional("DEBUG")]
        public void DropTrackedObjectAndCauseLeak(T trackedObject)
        {
            LeakTrackers.Remove(trackedObject);
        }

        private class LeakTracker : IDisposable
        {
            private volatile bool disposed;
            public volatile string Trace = null;

            public void Dispose()
            {
                this.disposed = true;
                GC.SuppressFinalize(this);
            }

            private string GetTrace()
            {
                return this.Trace ?? string.Empty;
            }

            ~LeakTracker()
            {
                if (!this.disposed && !Environment.HasShutdownStarted)
                {
                    var trace = this.GetTrace();
                }
            }
        }
    }
}
