using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FloxDc.CacheFlow;
using FloxDc.CacheFlow.Extensions;
using HappyTravel.Edo.Api.Infrastructure.DataProviders;
using HappyTravel.Edo.Api.Infrastructure.Options;
using HappyTravel.Edo.Api.Models.Agents;
using HappyTravel.Edo.Api.Services.Agents;
using HappyTravel.Edo.Common.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HappyTravel.Edo.Api.Services.Connectors
{
    public class SupplierConnectorManager : ISupplierConnectorManager
    {
        public SupplierConnectorManager(IOptions<SupplierOptions> options,
            IConnectorClient connectorClient,
            IServiceProvider serviceProvider)
        {
            _options = options.Value;
            _dataProviders = new Dictionary<Suppliers, ISupplierConnector>
            {
                // TODO: Add other data providers.
                {
                    Suppliers.Netstorming,
                    new SupplierConnector(connectorClient, _options.Netstorming, serviceProvider.GetRequiredService<ILogger<SupplierConnector>>())
                },
                {
                    Suppliers.Illusions,
                    new SupplierConnector(connectorClient, _options.Illusions, serviceProvider.GetRequiredService<ILogger<SupplierConnector>>())
                },
                {
                    Suppliers.Etg,
                    new SupplierConnector(connectorClient, _options.Etg, serviceProvider.GetRequiredService<ILogger<SupplierConnector>>())
                }
            };
        }

        public ISupplierConnector Get(Suppliers key) => _dataProviders[key];
        
        private readonly Dictionary<Suppliers, ISupplierConnector> _dataProviders;
        private readonly SupplierOptions _options;
    }
}