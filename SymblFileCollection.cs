using System;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Symbl.Concurrency
{
    public interface ISymblFileCollection
    {
        void Queue(string fullPath);
        void Dequeue(string fullPath);
        bool Peek(out string filePath);
    }

    /// <summary>
    /// This SymblFileCollection is thread-safe: more than one thread may enumerate the files presented by a 
    /// single instance of this class, and each thread will get all the files.
    /// </remarks>
    public sealed class SymblFileCollection : ISymblFileCollection, IEnumerable<string>
    {
        readonly CancellationToken _cancellationToken;
        readonly SymblFileQueue symblFileQueue;

        /// <summary>
        /// A queue of files found within one GetEnumerator call.
        /// </summary>
        private sealed class SymblFileQueue : IDisposable
        {
            readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
            readonly SemaphoreSlim _fileEnqueued = new SemaphoreSlim(0);

            /// <summary>
            /// Attempt to get a file from the queue.
            /// </summary>
            /// <param name="fileName">The name of the file, if one is immediately available.</param>
            /// <returns>True if got a file; false if not.</returns>
            public bool TryDequeue(out string fileName, 
                CancellationToken cancellationToken)
            {
                fileName = null;

                // Avoid the OperationCanceledException if we can.
                if (cancellationToken.IsCancellationRequested)
                    return false;
                try
                {
                    _fileEnqueued.Wait(cancellationToken);
                    return _queue.TryDequeue(out fileName);
                }
                catch (Exception ex)
                {
                    throw;
                }
            }

            public bool Peek(out string filePath)
            {
                var response = _queue.TryPeek(out filePath);
                return response;
            }

            /// <summary>
            /// Queue the file path
            /// </summary>
            public void Queue(string fullPath)
            {
                _queue.Enqueue(fullPath);
                _fileEnqueued.Release();
            }

            public void Dispose()
            {
                _fileEnqueued.Dispose();
            }
        }

        #region Constructor
        public SymblFileCollection(CancellationToken cancellationToken)
        {
            this._cancellationToken = cancellationToken;
            this.symblFileQueue = new SymblFileQueue();
        }
        #endregion

        #region Methods

        /// <summary>
        /// Get an enumerator that will yield files until the CanellationToken is canceled.
        /// </summary>
        /// </remarks>
        public IEnumerator<string> GetEnumerator()
        {
            if (!_cancellationToken.IsCancellationRequested)
            {
                if (!_cancellationToken.IsCancellationRequested)
                {
                    string fileName;
                    while (symblFileQueue.TryDequeue(out fileName,
                        _cancellationToken))
                        yield return fileName;
                }
            }
        }

        /// <summary>
        /// Queue the file path
        /// </summary>
        public void Queue(string fullPath)
        {
            symblFileQueue.Queue(fullPath);
        }

        /// <summary>
        /// Required method for IEnumerable.
        /// </summary>
        /// <returns>The generic enumerator, but as a non-generic version.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Dequeue(string fileName)
        {
            try
            {
                symblFileQueue.TryDequeue(out fileName, _cancellationToken);
            }
            catch(Exception ex)
            {
                throw;
            }           
        }

        public bool Peek(out string filePath)
        {
            try
            {
                var response = symblFileQueue.Peek(out filePath);
                Dequeue(filePath);
                return response;
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        #endregion
    }
}