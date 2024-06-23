Source code for the first alpha version of Scontreeno

- TwilioInput is the Azure functions which receives messages from Twilio API and processes attached media (jpg and pdf only)
- ReceiptInput is the Azure function which processes the uploaded media, analyze by using Azure Document Intelligence and gets back the response using Twilio API
