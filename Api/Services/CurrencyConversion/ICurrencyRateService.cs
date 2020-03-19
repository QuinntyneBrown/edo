using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.EdoContracts.General.Enums;

namespace HappyTravel.Edo.Api.Services.CurrencyConversion
{
    public interface ICurrencyRateService
    {
        ValueTask<Result<decimal>> Get(Currencies source, Currencies target);
    }
}