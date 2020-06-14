using System;
using HappyTravel.Edo.Common.Enums;

namespace HappyTravel.Edo.Data.Documents
{
    public interface IPaymentDocumentEntity : IEntity
    {
        ServiceTypes ServiceType { get; set; }
        string ParentReferenceCode { get; set; }
        string Data { get; set; }
        DateTime Date { get; set; }
        ServiceSource ServiceSource { get; set; }
        DocumentRegistrationInfo GetRegistrationInfo();
    }
}