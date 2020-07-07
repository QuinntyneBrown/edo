using System;
using System.Collections.Generic;
using HappyTravel.Edo.Api.Models.Payments.External.PaymentLinks;

namespace HappyTravel.Edo.Api.Infrastructure.Options
{
    public class PaymentLinkOptions
    {
        public string LinkMailTemplateId { get; set; }
        public string PaymentConfirmationMailTemplateId { get; set; }
        public ClientSettings ClientSettings { get; set; }
        public List<Version> SupportedVersions { get; set; }
        public Uri PaymentUrlPrefix { get; set; }
    }
}