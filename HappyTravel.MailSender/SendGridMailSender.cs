﻿using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.MailSender.Infrastructure;
using HappyTravel.MailSender.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace HappyTravel.MailSender
{
    public class SendGridMailSender : IMailSender
    {
        public SendGridMailSender(IOptions<SenderOptions> senderOptions, IHttpClientFactory httpClientFactory, ILogger<SendGridMailSender> logger)
        {
            _senderOptions = senderOptions.Value;
            _logger = logger ?? new NullLogger<SendGridMailSender>();
            _httpClientFactory = httpClientFactory;
        }


        public Task<Result> Send<TMessageData>(string templateId, string recipientAddress, TMessageData messageData)
            => Send(templateId, new[] {recipientAddress}, messageData);


        public async Task<Result> Send<TMessageData>(string templateId, IEnumerable<string> recipientAddresses, TMessageData messageData)
        {
            var enumerable = recipientAddresses as string[] ?? recipientAddresses.ToArray();
            if (!enumerable.Any())
                return Result.Fail("No recipient addresses provided");

            var templateData = GetTemplateData(templateId, messageData);
            using (var httpClient = _httpClientFactory.CreateClient(HttpClientName))
            {
                var client = new SendGridClient(httpClient, _senderOptions.ApiKey);
                try
                {
                    var result = Result.Ok();
                    foreach (var address in enumerable)
                    {
                        var message = new SendGridMessage
                        {
                            TemplateId = templateId,
                            From = _senderOptions.SenderAddress
                        };

                        message.SetTemplateData(templateData);
                        message.AddTo(address);

                        var response = await client.SendEmailAsync(message);
                        if (response.StatusCode == HttpStatusCode.Accepted)
                        {
                            _logger.LogSendMailInformation($"{templateId} successfully e-mailed to {address}");
                        }
                        else
                        {
                            var error = await response.Body.ReadAsStringAsync();
                            var failure =
                                $"Could not send an e-mail {templateId} to {address}, a server responded: '{error}' with status code '{response.StatusCode}'";
                            result = Result.Combine(result, Result.Fail(failure));

                            _logger.LogSendMailError(failure);
                        }

                        result = Result.Combine(result, Result.Ok());
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogSendMailException(ex);
                    return Result.Fail("Unhandled error occured while sending an e-mail.");
                }
            }
        }


        public static string HttpClientName = "SendGrid";


        private IDictionary<string, object> GetTemplateData<TMessageData>(string templateId, TMessageData messageData)
        {
            if (_templateData.TryGetValue(templateId, out var data))
                return data;

            var templateData = new ExpandoObject() as IDictionary<string, object>;
            templateData[_senderOptions.BaseUrlTemplateName] = _senderOptions.BaseUrl;
            if (messageData != null)
            {
                foreach (var propertyInfo in messageData.GetType().GetProperties())
                    templateData[propertyInfo.Name] = propertyInfo.GetValue(messageData, null);
            }

            _templateData.TryAdd(templateId, templateData);
            return templateData;
        }


        private readonly Dictionary<string, IDictionary<string, object>> _templateData = new Dictionary<string, IDictionary<string, object>>();

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SendGridMailSender> _logger;
        private readonly SenderOptions _senderOptions;
    }
}