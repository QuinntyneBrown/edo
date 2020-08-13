using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FloxDc.CacheFlow;
using FloxDc.CacheFlow.Extensions;
using HappyTravel.Edo.Api.Infrastructure;
using HappyTravel.Edo.Api.Infrastructure.Options;
using HappyTravel.Edo.Api.Models.Accommodations;
using HappyTravel.Edo.Api.Models.Agents;
using HappyTravel.Edo.Api.Models.Availabilities;
using HappyTravel.Edo.Api.Services.Accommodations.Mappings;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data.AccommodationMappings;
using HappyTravel.EdoContracts.Accommodations;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace HappyTravel.Edo.Api.Services.Accommodations.Availability
{
    public class AvailabilityStorage : IAvailabilityStorage
    {
        public AvailabilityStorage(IDistributedFlow distributedFlow,
            IMemoryFlow memoryFlow,
            IDateTimeProvider dateTimeProvider,
            IAccommodationDuplicatesService duplicatesService,
            IOptions<DataProviderOptions> options)
        {
            _distributedFlow = distributedFlow;
            _memoryFlow = memoryFlow;
            _dateTimeProvider = dateTimeProvider;
            _duplicatesService = duplicatesService;
            _providerOptions = options.Value;
        }


        public Task SaveResult(Guid searchId, DataProviders dataProvider, AvailabilityDetails details)
        {
            var timeStamp = _dateTimeProvider.UtcNow().Ticks;
            return SaveObject(searchId, new AvailabilityWithTimestamp(details, timeStamp), dataProvider);
        }


        public Task SetState(Guid searchId, DataProviders dataProvider, ProviderAvailabilitySearchState searchState)
            => SaveObject(searchId, searchState, dataProvider);


        public async Task<IEnumerable<ProviderData<AvailabilityResult>>> GetResult(Guid searchId, AgentContext agent)
        {
            var accommodationDuplicates = await _duplicatesService.Get(agent);
            
            var key = _memoryFlow.BuildKey(nameof(AvailabilityStorage), searchId.ToString());
            if (!_memoryFlow.TryGetValue(key, out List<(DataProviders DataProvider, AvailabilityWithTimestamp Result)> providerSearchResults))
            {
                providerSearchResults = (await GetProviderResults<AvailabilityWithTimestamp>(searchId))
                    .Where(t => !t.Result.Details.Equals(default))
                    .ToList();

                if ((await GetState(searchId)).TaskState == AvailabilitySearchTaskState.Completed)
                    _memoryFlow.Set(key, providerSearchResults, CacheExpirationTime);
            }

            return CombineAvailabilities(providerSearchResults);


            IEnumerable<ProviderData<AvailabilityResult>> CombineAvailabilities(List<(DataProviders ProviderKey, AvailabilityWithTimestamp Availability)> availabilities)
            {
                if (availabilities == null || !availabilities.Any())
                    return Enumerable.Empty<ProviderData<AvailabilityResult>>();

                return availabilities
                    .OrderBy(r => r.Availability.TimeStamp)
                    .SelectMany(providerResults =>
                    {
                        var (providerKey, providerAvailability) = providerResults;
                        var details = providerAvailability.Details;
                        var availabilityResults = details
                            .Results
                            .Select(accommodationAvailability =>
                            {
                                var minPrice = accommodationAvailability.RoomContractSets.Min(r => r.Price.NetTotal);
                                var maxPrice = accommodationAvailability.RoomContractSets.Max(r => r.Price.NetTotal);
                                var hasDuplicates = accommodationDuplicates.Contains(new ProviderAccommodationId(providerKey, accommodationAvailability.AccommodationDetails.Id));
                                
                                var result = new AvailabilityResult(providerAvailability.Details.AvailabilityId,
                                    accommodationAvailability.AccommodationDetails,
                                    accommodationAvailability.RoomContractSets,
                                    minPrice,
                                    maxPrice,
                                    hasDuplicates);

                                return ProviderData.Create(providerKey, result);
                            })
                            .ToList();

                        return availabilityResults;
                    });
            }
        }


        public async Task<AvailabilitySearchState> GetState(Guid searchId)
        {
            var providerSearchStates = await GetProviderResults<ProviderAvailabilitySearchState>(searchId);
            var searchStates = providerSearchStates
                .Where(s => !s.Result.Equals(default))
                .ToDictionary(s => s.DataProvider, s => s.Result);
            
            return AvailabilitySearchState.FromProviderStates(searchId, searchStates);
        }


        private Task<(DataProviders DataProvider, TObject Result)[]> GetProviderResults<TObject>(Guid searchId)
        {
            var providerTasks = _providerOptions
                .EnabledProviders
                .Select(async p =>
                {
                    var key = BuildKey<TObject>(searchId, p);
                    return (
                        ProviderKey: p,
                        Object: await _distributedFlow.GetAsync<TObject>(key)
                    );
                })
                .ToArray();

            return Task.WhenAll(providerTasks);
        }


        private Task SaveObject<TObjectType>(Guid searchId, TObjectType @object, DataProviders? dataProvider = null)
        {
            var key = BuildKey<TObjectType>(searchId, dataProvider);
            return _distributedFlow.SetAsync(key, @object, CacheExpirationTime);
        }


        private string BuildKey<TObjectType>(Guid searchId, DataProviders? dataProvider = null)
            => _distributedFlow.BuildKey(nameof(AvailabilityStorage),
                searchId.ToString(),
                typeof(TObjectType).Name,
                dataProvider?.ToString() ?? string.Empty);


        private static readonly TimeSpan CacheExpirationTime = TimeSpan.FromMinutes(15);

        private readonly IDistributedFlow _distributedFlow;
        private readonly IMemoryFlow _memoryFlow;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IAccommodationDuplicatesService _duplicatesService;
        private readonly DataProviderOptions _providerOptions;
        
        
        private readonly struct AvailabilityWithTimestamp
        {
            [JsonConstructor]
            public AvailabilityWithTimestamp(AvailabilityDetails details, long timeStamp)
            {
                TimeStamp = timeStamp;
                Details = details;
            }
            
            public long TimeStamp { get; }
            public AvailabilityDetails Details { get; }
        }
    }
}