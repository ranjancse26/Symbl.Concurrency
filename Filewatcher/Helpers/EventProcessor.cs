/*---------------------------------------------------------
 * Copyright (C) Microsoft Corporation. All rights reserved.
 *--------------------------------------------------------*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FileWatcherEx
{
    internal class EventProcessor
    {
        /// <summary>
        /// Aggregate and only emit events when changes have stopped for this duration (in ms)
        /// </summary>
        private static int EVENT_DELAY = 50;

        /// <summary>
        /// Warn after certain time span of event spam (in ticks)
        /// </summary>
        private static int EVENT_SPAM_WARNING_THRESHOLD = 60 * 1000 * 10000;

        private System.Object LOCK = new System.Object();
        private Task delayTask = null;

        private List<FileChangedEvent> events = new List<FileChangedEvent>();
        private Action<FileChangedEvent> handleEvent;

        private Action<string> logger;

        private long lastEventTime = 0;
        private long delayStarted = 0;

        private long spamCheckStartTime = 0;
        private bool spamWarningLogged = false;


        private IEnumerable<FileChangedEvent> NormalizeEvents(FileChangedEvent[] events)
        {
            var mapPathToEvents = new Dictionary<string, FileChangedEvent>();
            var eventsWithoutDuplicates = new List<FileChangedEvent>();

            // Normalize duplicates
            foreach (var newEvent in events)
            {
                mapPathToEvents.TryGetValue(newEvent.FullPath, out var oldEvent); // Try get event from newEvent.FullPath

                if (oldEvent != null && oldEvent.ChangeType == ChangeType.CREATED && newEvent.ChangeType == ChangeType.DELETED)
                { // CREATED + DELETED => remove
                    mapPathToEvents.Remove(oldEvent.FullPath);
                    eventsWithoutDuplicates.Remove(oldEvent);
                }
                else
                if (oldEvent != null && oldEvent.ChangeType == ChangeType.DELETED && newEvent.ChangeType == ChangeType.CREATED)
                { // DELETED + CREATED => CHANGED
                    oldEvent.ChangeType = ChangeType.CHANGED;
                }
                else
                if (oldEvent != null && oldEvent.ChangeType == ChangeType.CREATED && newEvent.ChangeType == ChangeType.CHANGED)
                { // CREATED + CHANGED => CREATED
                  // Do nothing
                }
                else
                { // Otherwise

                    if (newEvent.ChangeType == ChangeType.RENAMED)
                    { // If <ANY> + RENAMED
                        do
                        {
                            mapPathToEvents.TryGetValue(newEvent.OldFullPath, out var renameFromEvent); // Try get event from newEvent.OldFullPath

                            if (renameFromEvent != null && renameFromEvent.ChangeType == ChangeType.CREATED)
                            { // If rename from CREATED file
                              // Remove data about the CREATED file 
                                mapPathToEvents.Remove(renameFromEvent.FullPath);
                                eventsWithoutDuplicates.Remove(renameFromEvent);
                                // Handle new event as CREATED
                                newEvent.ChangeType = ChangeType.CREATED;
                                newEvent.OldFullPath = null;

                                if (oldEvent != null && oldEvent.ChangeType == ChangeType.DELETED)
                                { // DELETED + CREATED => CHANGED
                                    newEvent.ChangeType = ChangeType.CHANGED;
                                }
                            }
                            else
                            if (renameFromEvent != null && renameFromEvent.ChangeType == ChangeType.RENAMED)
                            { // If rename from RENAMED file
                              // Remove data about the RENAMED file 
                                mapPathToEvents.Remove(renameFromEvent.FullPath);
                                eventsWithoutDuplicates.Remove(renameFromEvent);
                                // Change OldFullPath
                                newEvent.OldFullPath = renameFromEvent.OldFullPath;
                                // Check again
                                continue;
                            }
                            else
                            { // Otherwise
                              // Do nothing
                              //mapPathToEvents.TryGetValue(newEvent.OldFullPath, out oldEvent); // Try get event from newEvent.OldFullPath
                            }
                        } while (false);
                    }

                    if (oldEvent != null)
                    { // If old event exists
                      // Replace old event data with data from the new event
                        oldEvent.ChangeType = newEvent.ChangeType;
                        oldEvent.OldFullPath = newEvent.OldFullPath;
                    }
                    else
                    { // If old event is not exist
                      // Add new event
                        mapPathToEvents.Add(newEvent.FullPath, newEvent);
                        eventsWithoutDuplicates.Add(newEvent);
                    }
                }
            }

            // Handle deletes
            var deletedPaths = new List<string>();

            // This algorithm will remove all DELETE events up to the root folder
            // that got deleted if any. This ensures that we are not producing
            // DELETE events for each file inside a folder that gets deleted.
            //
            // 1.) split ADD/CHANGE and DELETED events
            // 2.) sort short deleted paths to the top
            // 3.) for each DELETE, check if there is a deleted parent and ignore the event in that case

            return eventsWithoutDuplicates
                .Select((e, n) => new KeyValuePair<int, FileChangedEvent>(n, e)) // store original position value
                .OrderBy(e => e.Value.FullPath.Length) // shortest path first
                .Where(e =>
                {
                    if (e.Value.ChangeType == ChangeType.DELETED)
                    {
                        if (deletedPaths.Any(d => IsParent(e.Value.FullPath, d)))
                        {
                            return false; // DELETE is ignored if parent is deleted already
                        }

                    // otherwise mark as deleted
                    deletedPaths.Add(e.Value.FullPath);
                    }

                    return true;
                })
                .OrderBy(e => e.Key) // restore orinal position
                .Select(e => e.Value); //  remove unnecessary position value
        }


        private bool IsParent(string p, string candidate)
        {
            return p.IndexOf(candidate + '\\') == 0;
        }




        public EventProcessor(Action<FileChangedEvent> onEvent, Action<string> onLogging)
        {
            handleEvent = onEvent;
            logger = onLogging;
        }


        public void ProcessEvent(FileChangedEvent fileEvent)
        {
            lock (LOCK)
            {
                var now = DateTime.Now.Ticks;

                // Check for spam
                if (events.Count == 0)
                {
                    spamWarningLogged = false;
                    spamCheckStartTime = now;
                }
                else if (!spamWarningLogged && spamCheckStartTime + EVENT_SPAM_WARNING_THRESHOLD < now)
                {
                    spamWarningLogged = true;
                    logger(string.Format("Warning: Watcher is busy catching up wit {0} file changes in 60 seconds. Latest path is '{1}'", events.Count, fileEvent.FullPath));
                }

                // Add into our queue
                events.Add(fileEvent);
                lastEventTime = now;

                // Process queue after delay
                if (delayTask == null)
                {
                    // Create function to buffer events
                    Action<Task> func = null;
                    func = (Task value) =>
                    {
                        lock (LOCK)
                        {
                            // Check if another event has been received in the meantime
                            if (delayStarted == lastEventTime)
                            {
                                // Normalize and handle
                                var normalized = NormalizeEvents(events.ToArray());
                                foreach (var e in normalized)
                                {
                                    handleEvent(e);
                                }

                                // Reset
                                events.Clear();
                                delayTask = null;
                            }

                            // Otherwise we have received a new event while this task was
                            // delayed and we reschedule it.
                            else
                            {
                                delayStarted = lastEventTime;
                                delayTask = Task.Delay(EVENT_DELAY).ContinueWith(func);
                            }
                        }
                    };

                    // Start function after delay
                    delayStarted = lastEventTime;
                    delayTask = Task.Delay(EVENT_DELAY).ContinueWith(func);
                }
            }
        }
    }
}


