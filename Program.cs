using System;
using System.IO;
using System.Reflection;
using System.Threading;

using FileWatcherEx;
using Symbl.Concurrency.Model;
using Microsoft.Extensions.Configuration;

namespace Symbl.Concurrency
{
    class Program
    {
        private static ISymblDB symblDB;
        private static SymblFileCollection collection;
        private static SybmlAsyncProcessor symblAsyncProcessor;

        private static IConfigurationRoot configurationRoot;

        private static string appId;
        private static string appSecret;
        private static string mediaFolderPath;

        static void Main(string[] args)
        {
            configurationRoot = new ConfigurationBuilder()
                 .AddJsonFile("appSettings.json").Build();

            appId = configurationRoot["appId"];
            appSecret = configurationRoot["appSecret"];
            mediaFolderPath = configurationRoot["mediaFolderPath"];

            string exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            
            symblDB = new SymblDB($"URI=file:{exePath}\\sybml.db");
            symblDB.BuildDBModel();

            CancellationToken cancellationToken = new CancellationToken();
            collection = new SymblFileCollection(cancellationToken);

            InitCollection();

            symblAsyncProcessor = new SybmlAsyncProcessor(appId, 
                appSecret, collection, configurationRoot);

            System.Console.WriteLine("Started watching for files");

            var fileSystemWatcher = new FileSystemWatcherEx(mediaFolderPath);
            fileSystemWatcher.OnCreated += FileSystemWatcher_OnCreated;
            fileSystemWatcher.OnDeleted += FileSystemWatcher_OnDeleted;
            fileSystemWatcher.OnChanged += FileSystemWatcher_OnChanged;
            fileSystemWatcher.OnError += FileSystemWatcher_OnError;
            fileSystemWatcher.Start();

            var mediaTimerTrigger = new Timer(async (e) =>
            {
                await symblAsyncProcessor.ExecuteSybmlRequests();
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

            Console.WriteLine("Press any key to exit!");
            Console.ReadLine();
        }

        private static void InitCollection()
        {
            var files = Directory.GetFiles(mediaFolderPath);
            foreach(var file in files)
            {
                collection.Queue(file);
            }
        }

        private static void FileSystemWatcher_OnError(object sender, 
            System.IO.ErrorEventArgs e)
        {
            Console.WriteLine(e.GetException().StackTrace);
        }

        private static void FileSystemWatcher_OnChanged(object sender, 
            FileChangedEvent e)
        {
            try
            {
                // TODO: Validate the file path extension
                collection.Dequeue(e.OldFullPath);
                collection.Queue(e.FullPath);
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
            }
        }

        private static void FileSystemWatcher_OnDeleted(object sender,
            FileChangedEvent e)
        {
            try
            {
                collection.Dequeue(e.FullPath);
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
            }
        }

        private static void FileSystemWatcher_OnCreated(object sender, 
            FileChangedEvent e)
        {
            try
            {
                // TODO: Validate the file path extension
                collection.Queue(e.FullPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
