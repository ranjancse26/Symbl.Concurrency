/*---------------------------------------------------------
 * Copyright (C) Microsoft Corporation. All rights reserved.
 *--------------------------------------------------------*/

using System;
using System.IO;
using System.Collections.Generic;

namespace FileWatcherEx
{
    internal class FileWatcher : IDisposable
    {
        private string _watchPath = string.Empty;
        private Action<FileChangedEvent>? _eventCallback = null;
        private Dictionary<string, FileSystemWatcher> _fwDictionary = new Dictionary<string, FileSystemWatcher>();
        private Action<ErrorEventArgs>? _onError = null;

        /// <summary>
        /// Create new instance of FileSystemWatcher
        /// </summary>
        /// <param name="path">Full folder path to watcher</param>
        /// <param name="onEvent">onEvent callback</param>
        /// <param name="onError">onError callback</param>
        /// <returns></returns>
        public FileSystemWatcher Create(string path, Action<FileChangedEvent> onEvent, Action<ErrorEventArgs> onError)
        {
            _watchPath = path;
            _eventCallback = onEvent;
            _onError = onError;

            var watcher = new FileSystemWatcher
            {
                Path = _watchPath,
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite
                    | NotifyFilters.FileName
                    | NotifyFilters.DirectoryName,
            };

            // Bind internal events to manipulate the possible symbolic links
            watcher.Created += MakeWatcher_Created;
            watcher.Deleted += MakeWatcher_Deleted;

            watcher.Changed += ((object _, FileSystemEventArgs e) => ProcessEvent(e, ChangeType.CHANGED));
            watcher.Created += ((object _, FileSystemEventArgs e) => ProcessEvent(e, ChangeType.CREATED));
            watcher.Deleted += ((object _, FileSystemEventArgs e) => ProcessEvent(e, ChangeType.DELETED));
            watcher.Renamed += ((object _, RenamedEventArgs e) => ProcessEvent(e));
            watcher.Error += new ErrorEventHandler((object source, ErrorEventArgs e) => onError(e));

            //changing this to a higher value can lead into issues when watching UNC drives
            watcher.InternalBufferSize = 32768;
            _fwDictionary.Add(path, watcher);

            foreach (var dirInfo in new DirectoryInfo(path).GetDirectories())
            {
                var attrs = File.GetAttributes(dirInfo.FullName);

                // TODO: consider skipping hidden/system folders? 
                // See IG Issue #405 comment below
                // https://github.com/d2phap/ImageGlass/issues/405
                if (attrs.HasFlag(FileAttributes.Directory)
                    && attrs.HasFlag(FileAttributes.ReparsePoint))
                {
                    try
                    {
                        MakeWatcher(dirInfo.FullName);
                    }
                    catch
                    {
                        // IG Issue #405: throws exception on Windows 10
                        // for "c:\users\user\application data" folder and sub-folders.
                    }
                }
            }

            return watcher;
        }


        /// <summary>
        /// Process event for type = [CHANGED; DELETED; CREATED]
        /// </summary>
        /// <param name="e"></param>
        /// <param name="changeType"></param>
        private void ProcessEvent(FileSystemEventArgs e, ChangeType changeType)
        {
            _eventCallback?.Invoke(new FileChangedEvent
            {
                ChangeType = changeType,
                FullPath = e.FullPath,
            });
        }


        /// <summary>
        /// Process event for type = RENAMED
        /// </summary>
        /// <param name="e"></param>
        private void ProcessEvent(RenamedEventArgs e)
        {
            _eventCallback?.Invoke(new FileChangedEvent
            {
                ChangeType = ChangeType.RENAMED,
                FullPath = e.FullPath,
                OldFullPath = e.OldFullPath,
            });
        }


        private void MakeWatcher(string path)
        {
            if (!_fwDictionary.ContainsKey(path))
            {
                var fileSystemWatcherRoot = new FileSystemWatcher
                {
                    Path = path,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                // Bind internal events to manipulate the possible symbolic links
                fileSystemWatcherRoot.Created += MakeWatcher_Created;
                fileSystemWatcherRoot.Deleted += MakeWatcher_Deleted;

                fileSystemWatcherRoot.Changed += ((object _, FileSystemEventArgs e) => ProcessEvent(e, ChangeType.CHANGED));
                fileSystemWatcherRoot.Created += ((object _, FileSystemEventArgs e) => ProcessEvent(e, ChangeType.CREATED));
                fileSystemWatcherRoot.Deleted += ((object _, FileSystemEventArgs e) => ProcessEvent(e, ChangeType.DELETED));
                fileSystemWatcherRoot.Renamed += ((object _, RenamedEventArgs e) => ProcessEvent(e));
                fileSystemWatcherRoot.Error += ((object _, ErrorEventArgs e) => _onError?.Invoke(e));

                _fwDictionary.Add(path, fileSystemWatcherRoot);
            }

            foreach (var item in new DirectoryInfo(path).GetDirectories())
            {
                var attrs = File.GetAttributes(item.FullName);

                // If is a directory and symbolic link
                if (attrs.HasFlag(FileAttributes.Directory)
                    && attrs.HasFlag(FileAttributes.ReparsePoint))
                {
                    if (!_fwDictionary.ContainsKey(item.FullName))
                    {
                        var fswItem = new FileSystemWatcher
                        {
                            Path = item.FullName,
                            IncludeSubdirectories = true,
                            EnableRaisingEvents = true,
                        };

                        // Bind internal events to manipulate the possible symbolic links
                        fswItem.Created += (MakeWatcher_Created);
                        fswItem.Deleted += (MakeWatcher_Deleted);

                        fswItem.Changed += ((object _, FileSystemEventArgs e) => ProcessEvent(e, ChangeType.CHANGED));
                        fswItem.Created += ((object _, FileSystemEventArgs e) => ProcessEvent(e, ChangeType.CREATED));
                        fswItem.Deleted += ((object _, FileSystemEventArgs e) => ProcessEvent(e, ChangeType.DELETED));
                        fswItem.Renamed += ((object _, RenamedEventArgs e) => ProcessEvent(e));
                        fswItem.Error += ((object _, ErrorEventArgs e) => _onError?.Invoke(e));

                        _fwDictionary.Add(item.FullName, fswItem);
                    }

                    MakeWatcher(item.FullName);
                }
            }
        }


        private void MakeWatcher_Created(object sender, FileSystemEventArgs e)
        {
            try
            {
                var attrs = File.GetAttributes(e.FullPath);
                if (attrs.HasFlag(FileAttributes.Directory)
                    && attrs.HasFlag(FileAttributes.ReparsePoint))
                {
                    var watcherCreated = new FileSystemWatcher
                    {
                        Path = e.FullPath,
                        IncludeSubdirectories = true,
                        EnableRaisingEvents = true
                    };

                    // Bind internal events to manipulate the possible symbolic links
                    watcherCreated.Created += (MakeWatcher_Created);
                    watcherCreated.Deleted += (MakeWatcher_Deleted);

                    watcherCreated.Changed += ((object _, FileSystemEventArgs e) => ProcessEvent(e, ChangeType.CHANGED));
                    watcherCreated.Created += ((object _, FileSystemEventArgs e) => ProcessEvent(e, ChangeType.CREATED));
                    watcherCreated.Deleted += ((object _, FileSystemEventArgs e) => ProcessEvent(e, ChangeType.DELETED));
                    watcherCreated.Renamed += ((object _, RenamedEventArgs e) => ProcessEvent(e));
                    watcherCreated.Error += ((object _, ErrorEventArgs e) => _onError?.Invoke(e));

                    _fwDictionary.Add(e.FullPath, watcherCreated);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("<<ERROR>>: " + ex.Message);
            }
        }


        private void MakeWatcher_Deleted(object sender, FileSystemEventArgs e)
        {
            // If object removed, then I will dispose and remove them from dictionary
            if (_fwDictionary.ContainsKey(e.FullPath))
            {
                _fwDictionary[e.FullPath].Dispose();
                _fwDictionary.Remove(e.FullPath);
            }
        }

        /// <summary>
        /// Dispose the instance
        /// </summary>
        public void Dispose()
        {
            foreach (var item in _fwDictionary)
            {
                item.Value.Dispose();
            }
        }
    }
}


