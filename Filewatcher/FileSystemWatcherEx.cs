using System;
using System.IO;
using System.Threading;
using System.ComponentModel;
using System.Collections.Concurrent;

namespace FileWatcherEx
{
    /// <summary>
    /// Reused code https://github.com/d2phap/FileWatcherEx
    /// </summary>
    public class FileSystemWatcherEx : IDisposable
    {
        #region Private Properties

        private Thread _thread;
        private EventProcessor _processor;
        private BlockingCollection<FileChangedEvent> _fileEventQueue = new BlockingCollection<FileChangedEvent>();

        private FileWatcher _watcher = new FileWatcher();
        private FileSystemWatcher _fsw = new FileSystemWatcher();

        // Define the cancellation token.
        private CancellationTokenSource _cancelSource = new CancellationTokenSource();

        #endregion

        #region Public Properties

        /// <summary>
        /// Folder path to watch
        /// </summary>
        public string FolderPath { get; set; } = "";


        /// <summary>
        /// Filter string used for determining what files are monitored in a directory
        /// </summary>
        public string Filter { get; set; } = "*.*";


        /// <summary>
        /// Gets, sets the type of changes to watch for
        /// </summary>
        public NotifyFilters NotifyFilter { get; set; } = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;


        /// <summary>
        /// Gets or sets a value indicating whether subdirectories within the specified path should be monitored.
        /// </summary>
        public bool IncludeSubdirectories { get; set; } = false;


        /// <summary>
        /// Gets or sets the object used to marshal the event handler calls issued as a result of a directory change.
        /// </summary>
        public ISynchronizeInvoke SynchronizingObject { get; set; }

        #endregion

        #region Public Events
        public delegate void DelegateOnChanged(object sender, FileChangedEvent e);
        public event DelegateOnChanged OnChanged;

        public delegate void DelegateOnDeleted(object sender, FileChangedEvent e);
        public event DelegateOnDeleted OnDeleted;

        public delegate void DelegateOnCreated(object sender, FileChangedEvent e);
        public event DelegateOnCreated OnCreated;

        public delegate void DelegateOnRenamed(object sender, FileChangedEvent e);
        public event DelegateOnRenamed OnRenamed;

        public delegate void DelegateOnError(object sender, ErrorEventArgs e);
        public event DelegateOnError OnError;
        #endregion

        /// <summary>
        /// Initialize new instance of FileWatcherEx
        /// </summary>
        /// <param name="folder"></param>
        public FileSystemWatcherEx(string folder = "")
        {
            FolderPath = folder;
        }


        /// <summary>
        /// Start watching files
        /// </summary>
        public void Start()
        {
            if (!Directory.Exists(FolderPath)) return;


            _processor = new EventProcessor((e) =>
            {
                switch (e.ChangeType)
                {
                    case ChangeType.CHANGED:

                        InvokeChangedEvent(SynchronizingObject, e);

                        void InvokeChangedEvent(object sender, FileChangedEvent fileEvent)
                        {
                            if (SynchronizingObject != null && SynchronizingObject.InvokeRequired)
                            {
                                SynchronizingObject.Invoke(new Action<object, FileChangedEvent>(InvokeChangedEvent), new object[] { SynchronizingObject, e });
                            }
                            else
                            {
                                OnChanged?.Invoke(SynchronizingObject, e);
                            }
                        }


                        break;

                    case ChangeType.CREATED:

                        InvokeCreatedEvent(SynchronizingObject, e);

                        void InvokeCreatedEvent(object sender, FileChangedEvent fileEvent)
                        {
                            if (SynchronizingObject != null && SynchronizingObject.InvokeRequired)
                            {
                                SynchronizingObject.Invoke(new Action<object, FileChangedEvent>(InvokeCreatedEvent), new object[] { SynchronizingObject, e });
                            }
                            else
                            {
                                OnCreated?.Invoke(SynchronizingObject, e);
                            }
                        }


                        break;

                    case ChangeType.DELETED:

                        InvokeDeletedEvent(SynchronizingObject, e);

                        void InvokeDeletedEvent(object sender, FileChangedEvent fileEvent)
                        {
                            if (SynchronizingObject != null && SynchronizingObject.InvokeRequired)
                            {
                                SynchronizingObject.Invoke(new Action<object, FileChangedEvent>(InvokeDeletedEvent), new object[] { SynchronizingObject, e });
                            }
                            else
                            {
                                OnDeleted?.Invoke(SynchronizingObject, e);
                            }
                        }


                        break;

                    case ChangeType.RENAMED:

                        InvokeRenamedEvent(SynchronizingObject, e);

                        void InvokeRenamedEvent(object sender, FileChangedEvent fileEvent)
                        {
                            if (SynchronizingObject != null && SynchronizingObject.InvokeRequired)
                            {
                                SynchronizingObject.Invoke(new Action<object, FileChangedEvent>(InvokeRenamedEvent), new object[] { SynchronizingObject, e });
                            }
                            else
                            {
                                OnRenamed?.Invoke(SynchronizingObject, e);
                            }
                        }


                        break;

                    default:
                        break;
                }
            }, (log) =>
            {
                Console.WriteLine(string.Format("{0} | {1}", Enum.GetName(typeof(ChangeType), ChangeType.LOG), log));
            });

            _thread = new Thread(() => Thread_DoingWork(_cancelSource.Token))
            {
                // this ensures the thread does not block the process from terminating!
                IsBackground = true
            };

            _thread.Start();


            // Log each event in our special format to output queue
            void onEvent(FileChangedEvent e)
            {
                _fileEventQueue.Add(e);
            }


            // OnError
            void onError(ErrorEventArgs e)
            {
                if (e != null)
                {
                    OnError?.Invoke(this, e);
                }
            }


            // Start watcher
            _watcher = new FileWatcher();

            _fsw = _watcher.Create(FolderPath, onEvent, onError);
            _fsw.Filter = Filter;
            _fsw.NotifyFilter = NotifyFilter;
            _fsw.IncludeSubdirectories = IncludeSubdirectories;
            _fsw.SynchronizingObject = SynchronizingObject;

            // Start watching
            _fsw.EnableRaisingEvents = true;
        }

        private void Thread_DoingWork(CancellationToken cancelToken)
        {
            while (true)
            {
                if (cancelToken.IsCancellationRequested)
                    return;

                try
                {
                    var e = _fileEventQueue.Take(cancelToken);
                    _processor.ProcessEvent(e);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }


        /// <summary>
        /// Stop watching files
        /// </summary>
        public void Stop()
        {
            if (_fsw != null)
            {
                _fsw.EnableRaisingEvents = false;
            }

            if (_watcher != null)
            {
                _watcher.Dispose();
            }

            // stop the thread
            _cancelSource.Cancel();
        }



        /// <summary>
        /// Dispose the FileWatcherEx instance
        /// </summary>
        public void Dispose()
        {
            if (_fsw != null)
            {
                _fsw.Dispose();
            }
        }
    }
}