using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;

using Symbl.Concurrency.Model;
using SymblAISharp.Async.JobApi;
using SymblAISharp.Async.AudioApi;
using SymblAISharp.Async.VideoApi;
using SymblAISharp.Authentication;
using Microsoft.Extensions.Configuration;

namespace Symbl.Concurrency
{
    public interface ISybmlAsyncProcessor
    {
        Task ExecuteSybmlRequests();
    }

    /// <summary>
    /// The Symbl Async Processor deals with the processor of concurrent 
    /// media requests either it could be Video/Audio.
    /// </summary>
    public class SybmlAsyncProcessor : ISybmlAsyncProcessor
    {
        private string appId;
        private string appSecret;

        private ISymblDB symblDB;
        private SymblFileCollection collection;
        private IConfigurationRoot configurationRoot;

        public SybmlAsyncProcessor(string appId, string appSecret,
            SymblFileCollection collection,
            IConfigurationRoot configurationRoot)
        {
            this.appId = appId;
            this.appSecret = appSecret;
            this.collection = collection;
            this.configurationRoot = configurationRoot;

            string exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            symblDB = new SymblDB($"URI=file:{exePath}\\sybml.db");
            symblDB.BuildDBModel();
        }

        private string GetAccessToken()
        {
            AuthenticationApi authenticationApi = new AuthenticationApi();
            var authResponse = authenticationApi.GetAuthToken(new AuthRequest
            {
                appId = appId,
                appSecret = appSecret,
                type = "application"
            });

            if (authResponse != null)
                return authResponse.accessToken;

            return "";
        }

        private void Archive(string filePath)
        {
            string mediaArchivePath = configurationRoot["mediaArchiveFolderPath"];
            
            if (!Directory.Exists(mediaArchivePath))
                Directory.CreateDirectory(mediaArchivePath);

            if (File.Exists(filePath))
            {
                string fileName = Path.GetFileName(filePath);
                File.Move(filePath, $@"{mediaArchivePath}\{fileName}", true);
            }
        }

        public async Task ExecuteSybmlRequests()
        {
            // The preferred mechanism for updating the Job Status in DB is with the WebHook
            // However, as an alternate mechanism, you could also make use the Job Api.

            // 1. Get the Top 50 SybmlRequests from DB where the Job Status != Complete
            // 2. Do the Sybml Job Api request for each of the request id's
            // 3. Check whether or not the Job Status = Complete
            // 4. Get the count of Job Requests with the Status = Complete
            // 5. Perform additional Sybml Async Requests Based on the 
            // Number of completed job requests

            int numberOfRequestsToExecute = 0;

            var sybmlRequests = symblDB.GetTop50SymblRequests();

            string accessToken = GetAccessToken();

            List<string> jobResponses = new List<string>();

            foreach (var request in sybmlRequests)
            {
                IJobApi jobApi = new JobApi(accessToken);
                var jobResponse = await jobApi.GetJobResponse(request.RequestId);

                if (jobResponse != null)
                {
                    jobResponses.Add(jobResponse.status);
                    request.Status = jobResponse.status;
                    symblDB.Update(request);
                }                
            }

            if (sybmlRequests.Count == 0)
                numberOfRequestsToExecute = 50;
            else
            {
                numberOfRequestsToExecute = (from jobResponse in jobResponses
                                     where jobResponse == SybmlRequestStatus.Completed
                                     select jobResponse).ToList().Count;
            }

            // Make an additional async api requests
            // Update the SybmlRequests DB with the Job Id & Status

            // This code sample treats all sample *.mp4 as Videos and then
            // Uploads them to Sybml Video Api

            for (int i = 1; i <= numberOfRequestsToExecute; i++)
            {
                string filePath = "";
                if (collection.Peek(out filePath))
                {
                    // Check if File Exists and then Proceed
                    if (!File.Exists(filePath))
                        continue;

                    string extension = Path.GetExtension(filePath);
                    switch (extension.ToLower())
                    {
                        case "mp4":
                            var videoFileBytes = File.ReadAllBytes(filePath);
                            
                            IVideoApi videoApi = new VideoApi(accessToken);
                            var videoResponse = await videoApi.PostVideo(videoFileBytes, new VideoRequest
                            {
                                name = Path.GetFileName(filePath)
                            });

                            if(videoResponse != null)
                            {
                                symblDB.Insert(new SymblRequest
                                {
                                    RequestId = videoResponse.jobId,
                                    ConversationId = videoResponse.conversationId,
                                    Status = SybmlRequestStatus.InProgress
                                });

                                Archive(filePath);
                            }

                            break;
                        default:
                            var audioFileBytes = File.ReadAllBytes(filePath);

                            IAudioApi audioApi = new AudioApi(accessToken);
                            var audioResponse = await audioApi.PostAudio(audioFileBytes, new AudioRequest
                            {
                                name = Path.GetFileName(filePath)
                            });

                            if (audioResponse != null)
                            {
                                symblDB.Insert(new SymblRequest
                                {
                                    RequestId = audioResponse.jobId,
                                    ConversationId = audioResponse.conversationId,
                                    Status = SybmlRequestStatus.InProgress
                                });

                                Archive(filePath);
                            }
                            break;
                    }
                }
            }
        }
    }
}
