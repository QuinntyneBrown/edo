using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Infrastructure.Logging;
using HappyTravel.Edo.Api.Models.Accommodations;
using HappyTravel.Edo.Api.Models.Agents;
using HappyTravel.Edo.Api.Models.Locations;
using HappyTravel.Edo.Api.Services.Accommodations.Mappings;
using HappyTravel.Edo.Api.Services.Connectors;
using HappyTravel.Edo.Api.Services.Locations;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data.AccommodationMappings;
using HappyTravel.EdoContracts.Accommodations.Internals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AvailabilityRequest = HappyTravel.Edo.Api.Models.Availabilities.AvailabilityRequest;

namespace HappyTravel.Edo.Api.Services.Accommodations.Availability.Steps.WideAvailabilitySearch
{
    public class WideAvailabilitySearchService : IWideAvailabilitySearchService
    {
        public WideAvailabilitySearchService(IAccommodationDuplicatesService duplicatesService,
            ILocationService locationService,
            IDataProviderManager dataProviderManager,
            IWideAvailabilityStorage availabilityStorage,
            IServiceScopeFactory serviceScopeFactory,
            ILogger<WideAvailabilitySearchService> logger)
        {
            _duplicatesService = duplicatesService;
            _locationService = locationService;
            _dataProviderManager = dataProviderManager;
            _availabilityStorage = availabilityStorage;
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }
        
   
        public async Task<Result<Guid>> StartSearch(AvailabilityRequest request, AgentContext agent, string languageCode)
        {
            var searchId = Guid.NewGuid();
            _logger.LogMultiProviderAvailabilitySearchStarted($"Starting availability search with id '{searchId}'");
            
            var (_, isFailure, location, locationError) = await _locationService.Get(request.Location, languageCode);
            if (isFailure)
                return Result.Failure<Guid>(locationError.Detail);

            StartSearchTasks(searchId, request, await _dataProviderManager.GetEnabled(agent), location, agent, languageCode);
            
            return Result.Ok(searchId);
        }


        public async Task<WideAvailabilitySearchState> GetState(Guid searchId, AgentContext agent)
        {
            var searchStates = await _availabilityStorage.GetStates(searchId, await _dataProviderManager.GetEnabled(agent));
            return WideAvailabilitySearchState.FromProviderStates(searchId, searchStates);
        }
        
        public async Task<IEnumerable<WideAvailabilityResult>> GetResult(Guid searchId, AgentContext agent)
        {
            var accommodationDuplicates = await _duplicatesService.Get(agent);
            var providerSearchResults = await _availabilityStorage.GetResults(searchId, await _dataProviderManager.GetEnabled(agent));
            
            return CombineAvailabilities(providerSearchResults);

            IEnumerable<WideAvailabilityResult> CombineAvailabilities(IEnumerable<(DataProviders ProviderKey, List<AccommodationAvailabilityResult> AccommodationAvailabilities)> availabilities)
            {
                if (availabilities == null || !availabilities.Any())
                    return Enumerable.Empty<WideAvailabilityResult>();

                return availabilities
                    .SelectMany(providerResults =>
                    {
                        var (providerKey, providerAvailabilities) = providerResults;
                        return providerAvailabilities
                            .Select(pa => (Provider: providerKey, Availability: pa));
                    })
                    .OrderBy(r => r.Availability.Timestamp)
                    .RemoveRepeatedAccommodations()
                    .Select(r =>
                    {
                        var (provider, availability) = r;
                        var providerAccommodationId = new ProviderAccommodationId(provider, availability.Accommodation.Id);
                        var hasDuplicatesForCurrentAgent = accommodationDuplicates.Contains(providerAccommodationId);

                        return new WideAvailabilityResult(availability.Id,
                            availability.Accommodation,
                            availability.RoomContractSets,
                            availability.MinPrice,
                            availability.MaxPrice,
                            hasDuplicatesForCurrentAgent,
                            provider);
                    });
            }
        }

        private void StartSearchTasks(Guid searchId, AvailabilityRequest request, List<DataProviders> requestedProviders, Location location, AgentContext agent, string languageCode)
        {
            var contractsRequest = ConvertRequest(request, location);

            foreach (var provider in GetProvidersToSearch(location, requestedProviders))
            {
                Task.Run(async () =>
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    
                    await WideAvailabilitySearchTask
                        .Create(scope.ServiceProvider)
                        .Start(searchId, contractsRequest, provider, agent, languageCode);
                });
            }


            IReadOnlyCollection<DataProviders> GetProvidersToSearch(in Location location, List<DataProviders> dataProviders)
            {
                return location.DataProviders != null && location.DataProviders.Any()
                    ? location.DataProviders.Intersect(dataProviders).ToList()
                    : dataProviders;
            }


            static EdoContracts.Accommodations.AvailabilityRequest ConvertRequest(in AvailabilityRequest request, in Location location)
            {
                var roomDetails = request.RoomDetails
                    .Select(r => new RoomOccupationRequest(r.AdultsNumber, r.ChildrenAges, r.Type, r.IsExtraBedNeeded))
                    .ToList();

                return new EdoContracts.Accommodations.AvailabilityRequest(request.Nationality, request.Residency, request.CheckInDate,
                    request.CheckOutDate,
                    request.Filters, roomDetails,
                    new EdoContracts.GeoData.Location(location.Name, location.Locality, location.Country, location.Coordinates, location.Distance,
                        location.Source, location.Type),
                    request.PropertyType, request.Ratings);
            }
        }
        
        
        private readonly IAccommodationDuplicatesService _duplicatesService;
        private readonly ILocationService _locationService;
        private readonly IDataProviderManager _dataProviderManager;
        private readonly IWideAvailabilityStorage _availabilityStorage;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<WideAvailabilitySearchService> _logger;
    }
}