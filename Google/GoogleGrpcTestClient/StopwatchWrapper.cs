using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrpcTestClient
{
    public class StopwatchWrapper
    {
        private Stopwatch watch = new Stopwatch();

        public TimeSpan Elapsed => this.watch.Elapsed;
        public DateTime StartTime { get; private set; }
        public long ElapsedInUs => (long)(this.Elapsed.TotalMilliseconds * 1000);
        public double ElapsedInMs => this.Elapsed.TotalMilliseconds;

        public static StopwatchWrapper StartNew()
        {
            var tmp = new StopwatchWrapper();
            tmp.Start();
            return tmp;
        }

        public void Start()
        {
            this.StartTime = DateTime.Now;
            this.watch.Start();
        }

        public void Stop()
        {
            this.watch.Stop();
        }

        public void Reset()
        {
            this.watch.Reset();
        }

        public void Restart()
        {
            this.watch.Restart();
        }
    }
}
