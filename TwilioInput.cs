using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Scontreeno.Functions
{
    public class TwilioInput
    {
        string apiKeyId;
        string apiKeySecret;
        string storageConnectionString;
        string storageContainerName;

        private readonly ILogger<TwilioInput> _logger;

        public TwilioInput(ILogger<TwilioInput> logger)
        {
            _logger = logger;
            apiKeyId = Environment.GetEnvironmentVariable("TwilioAccountSid");
            apiKeySecret = Environment.GetEnvironmentVariable("TwilioAuthToken");
            storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            storageContainerName = Environment.GetEnvironmentVariable("ScontreenoStorageContainerName");
        }

        [Function("TwilioInput")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
        {
            var profileName = req.Form["ProfileName"];
            var attachedMedia = req.Form["NumMedia"];

            if(attachedMedia == "0")
                return new OkObjectResult($"Welcome to Scontreeno, {profileName}. Please, upload a receipt. I'll analyze and store it for you.");

            var mediaType = req.Form["MediaContentType0"];
            if(mediaType != "image/jpeg" && mediaType != "application/pdf")
                return new OkObjectResult($"I'm sorry, {profileName}. I can only process JPG images at the moment. Please send me an image. Thanks");

            var mediaURL = req.Form["MediaUrl0"];
            var waID = req.Form["WaId"];
            var from = req.Form["From"];
            if(String.IsNullOrEmpty(mediaURL)) return new OkObjectResult($"I'm sorry, {profileName}. I'm having problems in receiving image. Can you please try again?");
            try
            {
                Stream fileStream = await DownloadImageAsync(mediaURL);
                string filePath = $"{waID}/{from}/{Guid.NewGuid()}.jpg";
                await UploadToStorageAsync(filePath, fileStream);
                return new OkObjectResult($"Thanks, {profileName}. Your receipt has been received. 🥁 I'm processing it!");
            }            
            catch(Exception ex)
            {
                _logger.LogError(ex.Message);
                return new OkObjectResult($"I'm sorry, {profileName}. I'm having problems in receiving your media. Can you please try again?");
            }
        }

        private async Task<Stream> DownloadImageAsync(string URL) 
        {
            using(HttpClient cli = new HttpClient())
            {
                string auth = $"{apiKeyId}:{apiKeySecret}";
                cli.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes(auth)));
                var resp = await cli.GetAsync(URL);
                return await resp.Content.ReadAsStreamAsync();
            }
        }

        private async Task UploadToStorageAsync(string filePath, Stream stream)
        {
            BlobContainerClient client = new BlobContainerClient(storageConnectionString, storageContainerName);

            BlobClient blobClient = client.GetBlobClient(filePath);
            await blobClient.UploadAsync(stream, true);
        }

    }
}
