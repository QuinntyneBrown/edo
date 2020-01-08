﻿using HappyTravel.Edo.Common.Enums;
using Newtonsoft.Json;

namespace HappyTravel.Edo.Api.Models.Payments.Payfort
{
    public readonly struct CreditCardPaymentResult
    {
        [JsonConstructor]
        public CreditCardPaymentResult(string secure3d, string referenceCode, string authorizationCode, string externalCode, string expirationDate,
            string cardNumber, PaymentStatuses status, string message)
        {
            Secure3d = secure3d;
            ReferenceCode = referenceCode;
            AuthorizationCode = authorizationCode;
            ExternalCode = externalCode;
            ExpirationDate = expirationDate;
            CardNumber = cardNumber;
            Status = status;
            Message = message;
        }


        public CreditCardPaymentResult(PayfortPaymentResponse response, PaymentStatuses status) : this(
            response.Secure3d,
            response.SettlementReference,
            response.AuthorizationCode,
            response.FortId,
            response.ExpirationDate,
            response.CardNumber,
            status,
            $"{response.ResponseCode}: {response.ResponseMessage}")
        { }


        public string Secure3d { get; }
        public string ReferenceCode { get; }
        public string AuthorizationCode { get; }
        public string ExternalCode { get; }
        public string ExpirationDate { get; }
        public string CardNumber { get; }
        public PaymentStatuses Status { get; }
        public string Message { get; }
    }
}