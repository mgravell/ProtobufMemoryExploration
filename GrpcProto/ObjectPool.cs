using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace GrpcTestService
{
    /// <summary>
    /// An interface for object pools
    /// </summary>
    /// <typeparam name="T">Type of object to pool</typeparam>
    public interface IObjectPool<T>
    {
        /// <summary>
        /// Retrieve a new object of type T
        /// </summary>
        /// <returns>A pooled or newly instanced object of type T</returns>
        T Allocate();

        /// <summary>
        /// Return the object to the pool.
        /// </summary>
        /// <param name="value">An object of type T</param>
        void Free(T value);
    }

    /// <summary>
    /// A simple object pool with leak tracking.
    /// </summary>
    /// <typeparam name="T">Any reference type</typeparam>
    public class ObjectPool<T> : LeakTrackingObjectPool<T>, IObjectPool<T> where T : class
    {
        private readonly ConcurrentBag<T> bag = new ConcurrentBag<T>();
        private readonly Func<T> factory;
        private readonly Action<T> reset;

        private readonly int maxPoolCount;
        private long count;

        /// <summary>
        /// Creates a new ObjectPool
        /// </summary>
        /// <param name="factory">Method to allocate new T objects</param>
        /// <param name="maxPoolCount">Maximum size of the pool</param>
        /// <exception cref="ArgumentNullException">factory was null</exception>
        /// <exception cref="ArgumentOutOfRangeException">maxPoolCount was less than 1</exception>
        public ObjectPool(Func<T> factory, int maxPoolCount)
            : this(factory, null, maxPoolCount)
        {
        }

        /// <summary>
        /// Creates a new ObjectPool
        /// </summary>
        /// <param name="factory">Method to allocate new T objects</param>
        /// <param name="reset">A delegate for resetting the object. Can be null.</param>
        /// <param name="maxPoolCount">Maximum size of the pool</param>
        /// <exception cref="ArgumentNullException">factory was null</exception>
        /// <exception cref="ArgumentOutOfRangeException">maxPoolCount was less than 1</exception>
        public ObjectPool(Func<T> factory, Action<T> reset, int maxPoolCount)
        {
            if (maxPoolCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPoolCount), "maxPoolCount must be at least 1");
            }

            this.maxPoolCount = maxPoolCount;
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.reset = reset;
        }

        public int MaxPoolCount => this.maxPoolCount;

        /// <summary>
        /// Retrieve a new object of type T
        /// </summary>
        /// <returns>A pooled or newly instanced object of type T</returns>
        /// <exception cref="InvalidOperationException">The factory returned a null object.</exception>
        public T Allocate()
        {
            if (this.bag.TryTake(out T obj))
            {
                Interlocked.Decrement(ref this.count);
                return obj;
            }

            Console.WriteLine($"Cache miss of {typeof(T).Name}");

            obj = this.factory();
            if (obj == null)
            {
                throw new InvalidOperationException("Factory cannot return a null object");
            }

            this.TrackObject(obj);
            return obj;
        }

        /// <summary>
        /// Return the object to the pool.
        /// </summary>
        /// <param name="value">An object of type T</param>
        public void Free(T value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            this.reset?.Invoke(value);

            this.ForgetTrackedObject(value);
            this.Validate(value);

            // We use this interlocked code because calling ConcurrentBag.Count is a slow, contentious operation--it locks and iterates through
            // substructures to get the count.
            // Here we just check to make sure we're below our maximum size and then do and then increment.
            // Note that this doesn't strictly ensure that the pool never gets over maxPoolCount. It's possible for multiple threads to pass this
            // if check and add to the pool. That's ok--it's only temporary.
            if (Interlocked.Read(ref this.count) < this.maxPoolCount)
            {
                this.bag.Add(value);
                Interlocked.Increment(ref this.count);
            }
        }

        [Conditional("DEBUG")]
        private void Validate(object obj)
        {
            foreach (var item in this.bag)
            {
                if (object.ReferenceEquals(item, obj))
                {
                    throw new InvalidOperationException("Double free of object");
                }
            }
        }
    }
}
