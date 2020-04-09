using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Infrastructure.Options;
using HappyTravel.Edo.Api.Models.Agents;
using HappyTravel.Edo.Api.Models.Payments;
using HappyTravel.Edo.Api.Models.Payments.CreditCards;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data;
using HappyTravel.Edo.Data.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HappyTravel.Edo.Api.Services.Payments.CreditCards
{
    public class CreditCardService : ICreditCardService
    {
        public CreditCardService(EdoContext context, IOptions<PayfortOptions> options)
        {
            _context = context;
            _options = options.Value;
        }


        public async Task<List<CreditCardInfo>> Get(AgentInfo agentInfo)
        {
            var agentId = agentInfo.AgentId;
            var counterpartyId = agentInfo.CounterpartyId;
            var cards = await _context.CreditCards
                .Where(card => card.OwnerType == CreditCardOwnerType.Counterparty && card.OwnerId == counterpartyId ||
                    card.OwnerType == CreditCardOwnerType.Agent && card.OwnerId == agentId)
                .Select(ToCardInfo)
                .ToListAsync();

            return cards;
        }


        public Task Save(CreditCardInfo cardInfo, string token, AgentInfo agentInfo)
        {
            int ownerId;
            switch (cardInfo.OwnerType)
            {
                case CreditCardOwnerType.Counterparty:
                    ownerId = agentInfo.CounterpartyId;
                    break;
                case CreditCardOwnerType.Agent:
                    ownerId = agentInfo.AgentId;
                    break;
                default: throw new NotImplementedException();
            }
            
            var card = new CreditCard
            {
                ExpirationDate = cardInfo.ExpirationDate,
                HolderName = cardInfo.HolderName,
                MaskedNumber = cardInfo.Number,
                Token = token,
                OwnerId = ownerId,
                OwnerType = cardInfo.OwnerType
            };
            _context.CreditCards.Add(card);
            return _context.SaveChangesAsync();
        }


        public async Task<Result> Delete(int cardId, AgentInfo agentInfo)
        {
            var (_, isFailure, card, error) = await GetEntity(cardId, agentInfo);
            if (isFailure)
                return Result.Fail(error);

            _context.CreditCards.Remove(card);
            await _context.SaveChangesAsync();
            return Result.Ok();
        }


        public TokenizationSettings GetTokenizationSettings() => new TokenizationSettings(_options.AccessCode, _options.Identifier, _options.TokenizationUrl);

        public Task<Result<string>> GetToken(int cardId, AgentInfo agentInfo)
        {
            return GetCreditCard(cardId, agentInfo)
                .OnSuccess(c=> c.Token);
        }


        public Task<Result<CreditCardInfo>> Get(int cardId, AgentInfo agentInfo)
        {
            return GetCreditCard(cardId, agentInfo)
                .OnSuccess(ToCardInfoFunc);
        }


        private async Task<Result<CreditCard>> GetCreditCard(int cardId, AgentInfo agentInfo)
        {
            var card = await _context.CreditCards.SingleOrDefaultAsync(c => c.Id == cardId);
            if (card == null)
                return Result.Fail<CreditCard>($"Cannot find credit card by id {cardId}");

            if (card.OwnerType == CreditCardOwnerType.Counterparty && card.OwnerId != agentInfo.CounterpartyId ||
                card.OwnerType == CreditCardOwnerType.Agent && card.OwnerId != agentInfo.AgentId)
                Result.Fail<CreditCardInfo>("User doesn't have access to use this credit card");

            return Result.Ok(card);
        }


        private static Result<CreditCardInfo> MapCardInfo(CreditCard card) => Result.Ok(ToCardInfoFunc(card));


        private async Task<Result<CreditCard>> GetEntity(int cardId, AgentInfo agentInfo)
        {
            var card = await _context.CreditCards.FirstOrDefaultAsync(c => c.Id == cardId);
            if (card == null)
                return Result.Fail<CreditCard>($"Cannot find credit card by id {cardId}");

            if (card.OwnerType == CreditCardOwnerType.Counterparty && card.OwnerId != agentInfo.CounterpartyId ||
                card.OwnerType == CreditCardOwnerType.Agent && card.OwnerId != agentInfo.AgentId)
                Result.Fail<CreditCard>("User doesn't have access to use this credit card");

            return Result.Ok(card);
        }


        private static readonly Expression<Func<CreditCard, CreditCardInfo>> ToCardInfo = card =>
            new CreditCardInfo(card.Id, card.MaskedNumber, card.ExpirationDate, card.HolderName, card.OwnerType);

        private static readonly Func<CreditCard, CreditCardInfo> ToCardInfoFunc = ToCardInfo.Compile();

        private readonly EdoContext _context;
        private readonly PayfortOptions _options;
    }
}