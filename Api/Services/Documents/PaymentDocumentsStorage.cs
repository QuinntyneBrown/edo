using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data;
using HappyTravel.Edo.Data.Documents;
using Microsoft.EntityFrameworkCore;

namespace HappyTravel.Edo.Api.Services.Documents
{
    public class PaymentDocumentsStorage : IPaymentDocumentsStorage
    {
        public PaymentDocumentsStorage(EdoContext context,
            IDateTimeProvider dateTimeProvider)
        {
            _context = context;
            _dateTimeProvider = dateTimeProvider;
        }


        public async Task<DocumentRegistrationInfo> Register<TPaymentDocumentEntity>(TPaymentDocumentEntity documentEntity, Func<int, DateTime, string> numberGenerator)
            where TPaymentDocumentEntity : class, IPaymentDocumentEntity
        {
            var now = _dateTimeProvider.UtcNow();
            documentEntity.Date = now;
            documentEntity.Number = string.Empty;
            _context.Add(documentEntity);
            // Saving entity to fill it's id
            await _context.SaveChangesAsync();
            documentEntity.Number = numberGenerator(documentEntity.Id, now);
            await _context.SaveChangesAsync();
            
            return documentEntity.GetRegistrationInfo();
        }


        public Task<List<TPaymentDocumentEntity>> Get<TPaymentDocumentEntity>(ServiceTypes serviceType,
            ServiceSource serviceSource, string referenceCode)
            where TPaymentDocumentEntity : class, IPaymentDocumentEntity
        {
            return _context.Set<TPaymentDocumentEntity>()
                .Where(i => i.ParentReferenceCode == referenceCode &&
                    i.ServiceType == serviceType &&
                    i.ServiceSource == serviceSource)
                .OrderByDescending(i => i.Date)
                .ToListAsync();
        }


        public async Task<Result<TPaymentDocumentEntity>> Get<TPaymentDocumentEntity>(string number)
            where TPaymentDocumentEntity : class, IPaymentDocumentEntity
        {
            var document = await _context.Set<TPaymentDocumentEntity>()
                .SingleOrDefaultAsync(d => d.Number == number);

            return document == default
                ? Result.Failure<TPaymentDocumentEntity>("Could not find document")
                : Result.Success(document);
        }


        private readonly EdoContext _context;
        private readonly IDateTimeProvider _dateTimeProvider;
    }
}