using System.Collections.Generic;
using System.Threading.Tasks;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data.Documents;

namespace HappyTravel.Edo.Api.Services.Documents
{
    public class InvoiceService : IInvoiceService
    {
        public InvoiceService(IPaymentDocumentsStorage documentsStorage)
        {
            _documentsStorage = documentsStorage;
        }


        public Task<DocumentRegistrationInfo> Register<TInvoiceData>(ServiceTypes serviceType, ServiceSource serviceSource, string referenceCode,
            TInvoiceData data)
            => _documentsStorage
                .Register<TInvoiceData, Invoice>(serviceType, serviceSource, referenceCode, data);


        public Task<List<(DocumentRegistrationInfo Metadata, TInvoiceData Data)>> Get<TInvoiceData>(ServiceTypes serviceType, ServiceSource serviceSource,
            string referenceCode)
            => _documentsStorage.Get<TInvoiceData, Invoice>(serviceType, serviceSource, referenceCode);


        private readonly IPaymentDocumentsStorage _documentsStorage;
    }
}