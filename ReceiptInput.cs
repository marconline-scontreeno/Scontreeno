using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Google.Protobuf;
using System.Text;

namespace Scontreeno.Functions
{
    public class ReceiptInput
    {
        string apiKeyId;
        string apiKeySecret;
        string DI_Key;
        string DI_Endpoint;
        string storageConnectionString;
        string storageContainerName;
        private readonly ILogger<ReceiptInput> _logger;

        public ReceiptInput(ILogger<ReceiptInput> logger)
        {
            _logger = logger;
            apiKeyId = Environment.GetEnvironmentVariable("TwilioAccountSid");
            apiKeySecret = Environment.GetEnvironmentVariable("TwilioAuthToken");
            DI_Key = Environment.GetEnvironmentVariable("DI_KEY");
            DI_Endpoint = Environment.GetEnvironmentVariable("DI_Endpoint");
            storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            storageContainerName = storageContainerName = Environment.GetEnvironmentVariable("ScontreenoStorageContainerName");
        }

        [Function("ReceiptInput")]
        public async Task<IActionResult> Run([BlobTrigger("scontreeno/{waId}/{waFrom}/{filename}.jpg")] Stream myTriggerItem,
        string waId, string waFrom, string filename,
                FunctionContext context)
        {
                var logger = context.GetLogger("BlobFunction");
                logger.LogInformation($"DI KEY {DI_Key}");
                logger.LogInformation($"DI EP {DI_Endpoint}");

                TwilioClient.Init(apiKeyId, apiKeySecret);

                string message = null;

                try 
                {
                    AnalyzeResult result = await AnalyzeReceiptAsync(myTriggerItem);
                    message = GenerateResultMessage(result);
                }
                catch(Exception e)
                {
                    _logger.LogError(e.Message);
                    message = "I'm sorry. I couldn't analyze this receipt. With try with a different one. Thanks!";
                }

                var waMessage = await MessageResource.CreateAsync(
                    body: message,
                    from: new Twilio.Types.PhoneNumber("whatsapp:+14155238886"),
                    to: new Twilio.Types.PhoneNumber(waFrom));

                return new OkObjectResult("OK");
        }

        private async Task<Stream> DownloadBlobAsync(string blobPath)
        {
            Stream s = new MemoryStream();
            BlobContainerClient client = new BlobContainerClient(storageConnectionString, storageContainerName);
            BlobClient blobClient = client.GetBlobClient(blobPath);
            await blobClient.DownloadToAsync(s);
            s.Position = 0;
            return s;
        }

        private async Task<AnalyzeResult> AnalyzeReceiptAsync(Stream documentStream) 
        {
            AzureKeyCredential credential = new AzureKeyCredential(DI_Key);
            DocumentAnalysisClient client = new DocumentAnalysisClient(new Uri(DI_Endpoint), credential);

            AnalyzeDocumentOperation operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-receipt", documentStream);

            AnalyzeResult receipts = operation.Value;
            return receipts;
        }

        private string GenerateResultMessage(AnalyzeResult receipts)
        {
            var receipt = receipts.Documents[0];
            StringBuilder bld = new StringBuilder();
            bld.Append("It seems that you purchased:\n");

            if (receipt.Fields.TryGetValue("MerchantName", out DocumentField merchantNameField))
            {
                if (merchantNameField.FieldType == DocumentFieldType.String)
                {
                    string merchantName = merchantNameField.Value.AsString();
                    bld.Append($"-🏪 at *{merchantName}*\n");
                }
            }

            if (receipt.Fields.TryGetValue("TransactionDate", out DocumentField transactionDateField))
            {
                if (transactionDateField.FieldType == DocumentFieldType.Date)
                {
                    DateTimeOffset transactionDate = transactionDateField.Value.AsDate();
                    bld.Append($"-🗓 on the *{transactionDate}*\n");
                }
            }

            if (receipt.Fields.TryGetValue("Items", out DocumentField itemsField))
            {
                if (itemsField.FieldType == DocumentFieldType.List)
                {
                    List<string> itemNames = new List<string>();

                    foreach (DocumentField itemField in itemsField.Value.AsList())
                    {
                        if (itemField.FieldType == DocumentFieldType.Dictionary)
                        {
                            IReadOnlyDictionary<string, DocumentField> itemFields = itemField.Value.AsDictionary();

                            if (itemFields.TryGetValue("Description", out DocumentField itemDescriptionField))
                            {
                                if (itemDescriptionField.FieldType == DocumentFieldType.String)
                                {
                                    string itemDescription = itemDescriptionField.Value.AsString();
                                    if(itemNames.Count <= 5) itemNames.Add($"*{itemDescription.Replace("*","")}*");
                                }
                            }
                        }
                    }

                    if(itemNames.Count > 0) bld.Append($"-🛒 top 5 items: {String.Join(", ", itemNames)}");
                }
            }

            if (receipt.Fields.TryGetValue("Total", out DocumentField totalField))
            {
                if (totalField.FieldType == DocumentFieldType.Double)
                {
                    double total = totalField.Value.AsDouble();

                    bld.Append($"-🪙 with a total of: *{total}*");
                }
            }

            bld.Append("\n\nI'll add this receipt to your records. Thank you! 🔥🔥🔥");
            
            return bld.ToString();
        }

    }
}
