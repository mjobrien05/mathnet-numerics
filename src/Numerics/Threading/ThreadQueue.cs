﻿// <copyright file="ThreadQueue.cs" company="Math.NET">
// Math.NET Numerics, part of the Math.NET Project
// http://mathnet.opensourcedotnet.info
//
// Copyright (c) 2009 Math.NET
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
// </copyright>

namespace MathNet.Numerics.Threading
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// Internal Parallel Thread Queue.
    /// </summary>
    internal static class ThreadQueue
    {
        /// <summary>
        /// Sync Object for the thread queue state.
        /// </summary>
        private static readonly object _stateSync = new object();

        /// <summary>
        /// Sync Object for queue access (to be sure it's used by us only).
        /// </summary>
        private static readonly object _queueSync = new object();

        /// <summary>
        /// Queue holding the pending jobs.
        /// </summary>
        private static readonly Queue<Task> _queue = new Queue<Task>();

        /// <summary>
        /// Maximum number of jobs that can be in the queue at the same time. 
        /// </summary>
        private const int MaximumQueueLength = 1024;

        /// <summary>
        /// Counting Semaphore to make the worker thread wait for jobs
        /// </summary>
        private static Semaphore _tasksAvailableSemaphore;

        /// <summary>
        /// Running flag, used to signal worker threads to stop cleanly.
        /// </summary>
        private static bool _running = true;

        /// <summary>
        /// Worker threads
        /// </summary>
        private static Thread[] _threads;

        /// <summary>
        /// Gets the number of worker threads.
        /// </summary>
        internal static int ThreadCount { get; private set; }

        /// <summary>
        /// Initializes static members of the ThreadQueue class.
        /// </summary>
        static ThreadQueue()
        {
            Start(Environment.ProcessorCount);
        }

        /// <summary>
        /// Add a job to the queue.
        /// </summary>
        /// <param name="task">The job to run.</param>
        internal static void Enqueue(Task task)
        {
            if (!_running)
            {
                Start();
            }

            lock (_queueSync)
            {
                _queue.Enqueue(task);
            }

            _tasksAvailableSemaphore.Release();
        }

        /// <summary>
        /// Add a set of jobs to the queue.
        /// </summary>
        /// <param name="tasks">The jobs to run.</param>
        internal static void Enqueue(IList<Task> tasks)
        {
            if (!_running)
            {
                Start();
            }

            lock (_queueSync)
            {
                foreach (var task in tasks)
                {
                    _queue.Enqueue(task);
                }
            }

            _tasksAvailableSemaphore.Release(tasks.Count);
        }

        /// <summary>
        /// Worker Thread Program
        /// </summary>
        private static void WorkerThreadStart()
        {
            while (_running)
            {
                // Wait until a job is available, or we should shut down
                _tasksAvailableSemaphore.WaitOne();

                // Check whether we should shut down
                if (!_running)
                {
                    _tasksAvailableSemaphore.Release();
                    break;
                }

                // Get the job...
                Task task = null;
                lock (_queueSync)
                {
                    if (_queue.Count > 0)
                    {
                        task = _queue.Dequeue();
                    }
                }

                if (task == null)
                {
                    continue;
                }

                // ...and run it
                task.Compute();
                task.Set();
            }
        }

        /// <summary>
        /// Start or restart the queue with the specified number of worker threads.
        /// </summary>
        /// <param name="numberOfThreads">Number of worker threads.</param>
        internal static void Start(int numberOfThreads)
        {
            // instead of throwing an out of range exception, simply normalize
            numberOfThreads = Math.Max(1, Math.Min(1024, numberOfThreads));

            lock (_stateSync)
            {
                if (_threads != null)
                {
                    if (_threads.Length == numberOfThreads)
                    {
                        return;
                    }

                    Shutdown();
                }

                ThreadCount = numberOfThreads;
                Start();
            }
        }

        /// <summary>
        /// Start the thread queue, if it is not already running.
        /// </summary>
        internal static void Start()
        {
            lock (_stateSync)
            {
                if (_threads != null)
                {
                    return;
                }

                _tasksAvailableSemaphore = new Semaphore(_queue.Count, MaximumQueueLength);
                _running = true;
                _threads = new Thread[ThreadCount];

                for (var i = 0; i < _threads.Length; i++)
                {
                    _threads[i] = new Thread(WorkerThreadStart)
                                  {
                                      IsBackground = true
                                  };

                    _threads[i].Start();
                }
            }
        }

        /// <summary>
        /// Stop the thread queue, if it is running.
        /// </summary>
        internal static void Shutdown()
        {
            lock (_stateSync)
            {
                if (_threads == null)
                {
                    return;
                }

                // try to stop the worker threads cleanly
                _running = false;
                _tasksAvailableSemaphore.Release();

                // wait until all threads have stopped
                foreach (var thread in _threads)
                {
                    thread.Join();
                }

                _tasksAvailableSemaphore.Close();
                _tasksAvailableSemaphore = null;
                _threads = null;
            }
        }
    }
}
