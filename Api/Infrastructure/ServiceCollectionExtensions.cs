﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using FloxDc.CacheFlow;
using HappyTravel.Edo.Api.Filters.Authorization;
using HappyTravel.Edo.Api.Filters.Authorization.AdministratorFilters;
using HappyTravel.Edo.Api.Filters.Authorization.AgentExistingFilters;
using HappyTravel.Edo.Api.Filters.Authorization.CounterpartyStatesFilters;
using HappyTravel.Edo.Api.Filters.Authorization.InCounterpartyPermissionFilters;
using HappyTravel.Edo.Api.Infrastructure.Constants;
using HappyTravel.Edo.Api.Infrastructure.Converters;
using HappyTravel.Edo.Api.Infrastructure.DataProviders;
using HappyTravel.Edo.Api.Infrastructure.Environments;
using HappyTravel.Edo.Api.Infrastructure.Options;
using HappyTravel.Edo.Api.Models.Payments.External.PaymentLinks;
using HappyTravel.Edo.Api.Services.Accommodations;
using HappyTravel.Edo.Api.Services.Accommodations.Bookings;
using HappyTravel.Edo.Api.Services.Agents;
using HappyTravel.Edo.Api.Services.CodeProcessors;
using HappyTravel.Edo.Api.Services.Connectors;
using HappyTravel.Edo.Api.Services.CurrencyConversion;
using HappyTravel.Edo.Api.Services.Locations;
using HappyTravel.Edo.Api.Services.Mailing;
using HappyTravel.Edo.Api.Services.Management;
using HappyTravel.Edo.Api.Services.Markups;
using HappyTravel.Edo.Api.Services.Markups.Templates;
using HappyTravel.Edo.Api.Services.Payments;
using HappyTravel.Edo.Api.Services.Payments.Accounts;
using HappyTravel.Edo.Api.Services.Payments.CreditCards;
using HappyTravel.Edo.Api.Services.Payments.External;
using HappyTravel.Edo.Api.Services.Payments.External.PaymentLinks;
using HappyTravel.Edo.Api.Services.Payments.Payfort;
using HappyTravel.Edo.Api.Services.ProviderResponses;
using HappyTravel.Edo.Api.Services.SupplierOrders;
using HappyTravel.Edo.Api.Services.Users;
using HappyTravel.Edo.Api.Services.Versioning;
using HappyTravel.Edo.Common.Enums;
using HappyTravel.Edo.Data;
using HappyTravel.EdoContracts.General.Enums;
using HappyTravel.Geography;
using HappyTravel.MailSender;
using HappyTravel.MailSender.Infrastructure;
using HappyTravel.VaultClient;
using IdentityServer4.AccessTokenValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Localization.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetTopologySuite;
using Newtonsoft.Json;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace.Configuration;
using OpenTelemetry.Trace.Samplers;
using Polly;
using Polly.Extensions.Http;
using SendGrid.Helpers.Mail;

namespace HappyTravel.Edo.Api.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection ConfigureAuthentication(this IServiceCollection services, IConfiguration configuration,
            IWebHostEnvironment environment, IVaultClient vaultClient)
        {
            var (apiName, authorityUrl) = GetApiNameAndAuthority(configuration, environment, vaultClient);

            services.AddAuthentication(IdentityServerAuthenticationDefaults.AuthenticationScheme)
                .AddIdentityServerAuthentication(options =>
                {
                    options.Authority = authorityUrl;
                    options.ApiName = apiName;
                    options.RequireHttpsMetadata = true;
                    options.SupportedTokens = SupportedTokens.Jwt;
                });

            return services;
        }


        public static IServiceCollection ConfigureHttpClients(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment, IVaultClient vaultClient)
        {
            var (_, authorityUrl) = GetApiNameAndAuthority(configuration, environment, vaultClient);
            services.AddHttpClient(HttpClientNames.OpenApiDiscovery, client => client.BaseAddress = new Uri(authorityUrl));

            services.AddHttpClient(HttpClientNames.OpenApiUserInfo);

            services.AddHttpClient(HttpClientNames.GoogleMaps, c => { c.BaseAddress = new Uri(configuration["Edo:Google:Endpoint"]); })
                .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                .AddPolicyHandler(GetDefaultRetryPolicy());

            services.AddHttpClient(SendGridMailSender.HttpClientName)
                .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                .AddPolicyHandler(GetDefaultRetryPolicy());

            services.AddHttpClient(HttpClientNames.Payfort)
                .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                .AddPolicyHandler(GetDefaultRetryPolicy());
            
            services.AddHttpClient(HttpClientNames.CurrencyService)
                .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                .AddPolicyHandler(GetDefaultRetryPolicy());

            return services;
        }


        public static IServiceCollection ConfigureServiceOptions(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment, VaultClient.VaultClient vaultClient)
        {
            #region mailing setting

            var mailSettings = vaultClient.Get(configuration["Edo:Email:Options"]).GetAwaiter().GetResult();
            var edoPublicUrl = mailSettings[configuration["Edo:Email:EdoPublicUrl"]];

            var sendGridApiKey = mailSettings[configuration["Edo:Email:ApiKey"]];
            var senderAddress = mailSettings[configuration["Edo:Email:SenderAddress"]];
            services.Configure<SenderOptions>(options =>
            {
                options.ApiKey = sendGridApiKey;
                options.BaseUrl = edoPublicUrl;
                options.SenderAddress = new EmailAddress(senderAddress);
            });

            var agentInvitationTemplateId = mailSettings[configuration["Edo:Email:AgentInvitationTemplateId"]];
            services.Configure<AgentInvitationOptions>(options =>
            {
                options.MailTemplateId = agentInvitationTemplateId;
                options.EdoPublicUrl = edoPublicUrl;
            });

            var administratorInvitationTemplateId = mailSettings[configuration["Edo:Email:AdministratorInvitationTemplateId"]];
            services.Configure<AdministratorInvitationOptions>(options =>
            {
                options.MailTemplateId = administratorInvitationTemplateId;
                options.EdoPublicUrl = edoPublicUrl;
            });
            services.Configure<UserInvitationOptions>(options =>
                options.InvitationExpirationPeriod = TimeSpan.FromDays(7));

            var administrators = JsonConvert.DeserializeObject<List<string>>(mailSettings[configuration["Edo:Email:Administrators"]]);
            var masterAgentRegistrationMailTemplateId = mailSettings[configuration["Edo:Email:MasterAgentRegistrationTemplateId"]];
            var regularAgentRegistrationMailTemplateId = mailSettings[configuration["Edo:Email:RegularAgentRegistrationTemplateId"]];
            services.Configure<AgentRegistrationNotificationOptions>(options =>
            {
                options.AdministratorsEmails = administrators;
                options.MasterAgentMailTemplateId = masterAgentRegistrationMailTemplateId;
                options.RegularAgentMailTemplateId = regularAgentRegistrationMailTemplateId;
            });

            var bookingVoucherTemplateId = mailSettings[configuration["Edo:Email:BookingVoucherTemplateId"]];
            var bookingInvoiceTemplateId = mailSettings[configuration["Edo:Email:BookingInvoiceTemplateId"]];
            var bookingCancelledTemplateId = mailSettings[configuration["Edo:Email:BookingCancelledTemplateId"]];
            services.Configure<BookingMailingOptions>(options =>
            {
                options.VoucherTemplateId = bookingVoucherTemplateId;
                options.InvoiceTemplateId = bookingInvoiceTemplateId;
                options.BookingCancelledTemplateId = bookingCancelledTemplateId;
            });

            var knownCustomerTemplateId = mailSettings[configuration["Edo:Email:KnownCustomerBillTemplateId"]];
            var unknownCustomerTemplateId = mailSettings[configuration["Edo:Email:UnknownCustomerBillTemplateId"]];
            var needPaymentTemplateId = mailSettings[configuration["Edo:Email:NeedPaymentTemplateId"]];
            services.Configure<PaymentNotificationOptions>(po =>
            {
                po.KnownCustomerTemplateId = knownCustomerTemplateId;
                po.UnknownCustomerTemplateId = unknownCustomerTemplateId;
                po.NeedPaymentTemplateId = needPaymentTemplateId;
            });

            #endregion

            var databaseOptions = vaultClient.Get(configuration["Edo:Database:Options"]).GetAwaiter().GetResult();
            services.AddEntityFrameworkNpgsql().AddDbContextPool<EdoContext>(options =>
            {
                var host = databaseOptions["host"];
                var port = databaseOptions["port"];
                var password = databaseOptions["password"];
                var userId = databaseOptions["userId"];

                var connectionString = configuration.GetConnectionString("Edo");
                options.UseNpgsql(string.Format(connectionString, host, port, userId, password), builder =>
                {
                    builder.UseNetTopologySuite();
                    builder.EnableRetryOnFailure();
                });
                options.EnableSensitiveDataLogging(false);
                options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            }, 16);
                
            var currencyConverterOptions = vaultClient.Get(configuration["CurrencyConverter:Options"]).GetAwaiter().GetResult();
            services.Configure<CurrencyRateServiceOptions>(o =>
            {
                var url = environment.IsLocal()
                    ? configuration["CurrencyConverter:Url"]
                    : currencyConverterOptions["url"];

                o.ServiceUrl = new Uri(url);

                var cacheLifeTimeMinutes = environment.IsLocal()
                    ? configuration["CurrencyConverter:CacheLifetimeInMinutes"]
                    : currencyConverterOptions["cacheLifetimeMinutes"];

                o.CacheLifeTime = TimeSpan.FromMinutes(int.Parse(cacheLifeTimeMinutes));
            });

            var dataProvidersOptions = vaultClient.Get(configuration["DataProviders:Options"]).GetAwaiter().GetResult();
            services.Configure<DataProviderOptions>(options =>
            {
                var netstormingEndpoint = environment.IsLocal()
                    ? configuration["DataProviders:NetstormingConnector"]
                    : dataProvidersOptions["netstormingConnector"];

                options.Netstorming = netstormingEndpoint;

                var illusionsEndpoint = environment.IsLocal()
                    ? configuration["DataProviders:Illusions"]
                    : dataProvidersOptions["illusions"];

                options.Illusions = illusionsEndpoint;

                var enabledConnectors = environment.IsLocal()
                    ? configuration["DataProviders:EnabledConnectors"]
                    : dataProvidersOptions["enabledConnectors"];

                options.EnabledProviders = enabledConnectors
                    .Split(';')
                    .Select(c => c.Trim())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(Enum.Parse<Common.Enums.DataProviders>)
                    .ToList();
            });

            var googleOptions = vaultClient.Get(configuration["Edo:Google:Options"]).GetAwaiter().GetResult();
            services.Configure<GoogleOptions>(options => { options.ApiKey = googleOptions["apiKey"]; })
                .Configure<FlowOptions>(options =>
                {
                    options.CacheKeyDelimiter = "::";
                    options.CacheKeyPrefix = "HappyTravel::Edo::Api";
                })
                .Configure<RequestLocalizationOptions>(options =>
                {
                    options.DefaultRequestCulture = new RequestCulture("en");
                    options.SupportedCultures = new[]
                    {
                        new CultureInfo("en"),
                        new CultureInfo("ar"),
                        new CultureInfo("ru")
                    };

                    options.RequestCultureProviders.Insert(0, new RouteDataRequestCultureProvider {Options = options});
                });

            services.Configure<LocationServiceOptions>(o =>
            {
                o.IsGoogleGeoCoderDisabled = bool.TryParse(googleOptions["disabled"], out var disabled) && disabled;
            });

            var paymentLinksOptions = vaultClient.Get(configuration["PaymentLinks:Options"]).GetAwaiter().GetResult();
            var externalPaymentsMailTemplateId = mailSettings[configuration["Edo:Email:ExternalPaymentsTemplateId"]];
            services.Configure<PaymentLinkOptions>(options =>
            {
                options.ClientSettings = new ClientSettings
                {
                    Currencies = configuration.GetSection("PaymentLinks:Currencies")
                        .Get<List<Currencies>>(),
                    ServiceTypes = configuration.GetSection("PaymentLinks:ServiceTypes")
                        .Get<Dictionary<ServiceTypes, string>>()
                };
                options.MailTemplateId = externalPaymentsMailTemplateId;
                options.SupportedVersions = new List<Version> {new Version(0, 2)};
                options.PaymentUrlPrefix = new Uri(paymentLinksOptions["endpoint"]);
            });

            var payfortOptions = vaultClient.Get(configuration["Edo:Payfort:Options"]).GetAwaiter().GetResult();
            var payfortUrlsOptions = vaultClient.Get(configuration["Edo:Payfort:Urls"]).GetAwaiter().GetResult();
            services.Configure<PayfortOptions>(options =>
            {
                options.AccessCode = payfortOptions["access-code"];
                options.Identifier = payfortOptions["merchant-identifier"];
                options.ShaRequestPhrase = payfortOptions["request-phrase"];
                options.ShaResponsePhrase = payfortOptions["response-phrase"];
                options.PaymentUrl = payfortUrlsOptions["payment"];
                options.TokenizationUrl = payfortUrlsOptions["tokenization"];
                options.ReturnUrl = payfortUrlsOptions["return"];
                options.ResultUrl = payfortUrlsOptions["result"];
            });

            return services;
        }


        public static IServiceCollection AddServices(this IServiceCollection services)
        {
            services.AddSingleton(NtsGeometryServices.Instance.CreateGeometryFactory(GeoConstants.SpatialReferenceId));

            services.AddTransient<IDataProviderClient, DataProviderClient>();
            services.AddTransient<ICountryService, CountryService>();
            services.AddTransient<IGeoCoder, GoogleGeoCoder>();
            services.AddTransient<IGeoCoder, InteriorGeoCoder>();
            
            services.AddSingleton<IVersionService, VersionService>();

            services.AddTransient<ILocationService, LocationService>();
            services.AddTransient<ICounterpartyService, CounterpartyService>();
            services.AddTransient<IAgentService, AgentService>();
            services.AddTransient<IAgentRegistrationService, AgentRegistrationService>();
            services.AddTransient<IAccountPaymentService, AccountPaymentService>();
            services.AddTransient<IPaymentSettingsService, PaymentSettingsService>();
            services.AddTransient<IBookingPaymentService, BookingPaymentService>();
            services.AddTransient<IAccommodationService, AccommodationService>();
            services.AddScoped<IAgentContext, HttpBasedAgentContext>();
            services.AddScoped<IAgentContextInternal, HttpBasedAgentContext>();
            services.AddHttpContextAccessor();
            services.AddSingleton<IDateTimeProvider, DefaultDateTimeProvider>();
            services.AddSingleton<IAvailabilityResultsCache, AvailabilityResultsCache>();
            services.AddTransient<IBookingManager, BookingManager>();
            services.AddTransient<ITagProcessor, TagProcessor>();

            services.AddTransient<IAgentInvitationService, AgentInvitationService>();
            services.AddSingleton<IMailSender, SendGridMailSender>();
            services.AddSingleton<ITokenInfoAccessor, TokenInfoAccessor>();
            services.AddTransient<IAccountBalanceAuditService, AccountBalanceAuditService>();
            services.AddTransient<ICreditCardAuditService, CreditCardAuditService>();

            services.AddTransient<IAccountManagementService, AccountManagementService>();
            services.AddScoped<IAdministratorContext, HttpBasedAdministratorContext>();
            services.AddScoped<IServiceAccountContext, HttpBasedServiceAccountContext>();

            services.AddTransient<IUserInvitationService, UserInvitationService>();
            services.AddTransient<IAdministratorInvitationService, AdministratorInvitationService>();
            services.AddTransient<IExternalAdminContext, ExternalAdminContext>();

            services.AddTransient<IAdministratorRegistrationService, AdministratorRegistrationService>();
            services.AddScoped<IManagementAuditService, ManagementAuditService>();

            services.AddScoped<IEntityLocker, EntityLocker>();
            services.AddTransient<IAccountPaymentProcessingService, AccountPaymentProcessingService>();

            services.AddTransient<IPayfortService, PayfortService>();
            services.AddTransient<ICreditCardsManagementService, CreditCardsManagementService>();
            services.AddTransient<IPayfortSignatureService, PayfortSignatureService>();

            services.AddTransient<IMarkupService, MarkupService>();

            services.AddSingleton<IMarkupPolicyTemplateService, MarkupPolicyTemplateService>();
            services.AddScoped<IMarkupPolicyManager, MarkupPolicyManager>();

            services.AddScoped<ICurrencyRateService, CurrencyRateService>();
            services.AddScoped<ICurrencyConverterService, CurrencyConverterService>();

            services.AddTransient<ISupplierOrderService, SupplierOrderService>();
            services.AddTransient<IMarkupLogger, MarkupLogger>();

            services.AddSingleton<IJsonSerializer, NewtonsoftJsonSerializer>();
            services.AddTransient<IAgentSettingsManager, AgentSettingsManager>();

            services.AddTransient<IPaymentLinkService, PaymentLinkService>();
            services.AddTransient<IPaymentLinksProcessingService, PaymentLinksProcessingService>();
            services.AddTransient<IPaymentCallbackDispatcher, PaymentCallbackDispatcher>();
            services.AddTransient<IAgentPermissionManagementService, AgentPermissionManagementService>();
            services.AddTransient<IPermissionChecker, PermissionChecker>();
            services.AddTransient<IPaymentNotificationService, PaymentNotificationService>();
            services.AddTransient<IBookingMailingService, BookingMailingService>();
            services.AddTransient<IPaymentHistoryService, PaymentHistoryService>();
            services.AddTransient<IBookingDocumentsService, BookingDocumentsService>();
            services.AddTransient<INetstormingResponseService, NetstormingResponseService>();
            services.AddTransient<IBookingAuditLogService, BookingAuditLogService>();
            services.AddTransient<IDataProviderFactory, DataProviderFactory>();
            services.AddTransient<IAvailabilityService, AvailabilityService>();
            services.AddTransient<IBookingService, BookingService>();
            services.AddTransient<IBookingsProcessingService, BookingsProcessingService>();
            services.AddTransient<IProviderRouter, ProviderRouter>();

            services.AddSingleton<IAuthorizationPolicyProvider, CustomAuthorizationPolicyProvider>();
            services.AddTransient<IAuthorizationHandler, InCounterpartyPermissionAuthorizationHandler>();
            services.AddTransient<IAuthorizationHandler, MinCounterpartyStateAuthorizationHandler>();
            services.AddTransient<IAuthorizationHandler, AdministratorPermissionsAuthorizationHandler>();
            services.AddTransient<IAuthorizationHandler, AgentRequiredAuthorizationHandler>();

            services.AddTransient<ICreditCardPaymentProcessingService, CreditCardPaymentProcessingService>();
            services.AddTransient<ICreditCardMoneyAuthorizationService, CreditCardMoneyAuthorizationService>();
            services.AddTransient<ICreditCardMoneyCaptureService, CreditCardMoneyCaptureService>();
            services.AddTransient<IPayfortResponseParser, PayfortResponseParser>();
            
            // Default behaviour allows not authenticated requests to be checked by authorization policies.
            // Special wrapper returns Forbid result for them.
            // More information: https://github.com/dotnet/aspnetcore/issues/4656
            services.AddTransient<IPolicyEvaluator, ForbidUnauthenticatedPolicyEvaluator>();
            // Default policy evaluator needs to be registered as dependency of ForbidUnauthenticatedPolicyEvaluator.
            services.AddTransient<PolicyEvaluator>();

            return services;
        }


        public static IServiceCollection AddTracing(this IServiceCollection services, IWebHostEnvironment environment, IConfiguration configuration)
        {
            string agentHost;
            int agentPort;
            if (environment.IsLocal())
            {
                agentHost = configuration["Jaeger:AgentHost"];
                agentPort = int.Parse(configuration["Jaeger:AgentPort"]);
            }
            else
            {
                agentHost = EnvironmentVariableHelper.Get("Jaeger:AgentHost", configuration);
                agentPort = int.Parse(EnvironmentVariableHelper.Get("Jaeger:AgentPort", configuration));
            }
            
            var serviceName = $"{environment.ApplicationName}-{environment.EnvironmentName}";
            services.AddOpenTelemetry(builder =>
            {
                builder.UseJaeger(options =>
                    {
                        options.ServiceName = serviceName;
                        options.AgentHost = agentHost;
                        options.AgentPort = agentPort;
                    })
                    .AddRequestCollector()
                    .AddDependencyCollector()
                    .SetResource(Resources.CreateServiceResource(serviceName))
                    .SetSampler(new AlwaysOnSampler());
            });

            return services;
        }


        private static (string apiName, string authorityUrl) GetApiNameAndAuthority(IConfiguration configuration, IWebHostEnvironment environment, IVaultClient vaultClient)
        {
            var authorityOptions = vaultClient.Get(configuration["Authority:Options"]).GetAwaiter().GetResult();

            var apiName = configuration["Authority:ApiName"];
            var authorityUrl = configuration["Authority:Endpoint"];
            if (environment.IsDevelopment() || environment.IsLocal())
                return (apiName, authorityUrl);

            apiName = authorityOptions["apiName"];
            authorityUrl = authorityOptions["authorityUrl"];

            return (apiName, authorityUrl);
        }


        private static IAsyncPolicy<HttpResponseMessage> GetDefaultRetryPolicy()
        {
            var jitter = new Random();

            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(3, attempt
                    => TimeSpan.FromMilliseconds(Math.Pow(500, attempt)) + TimeSpan.FromMilliseconds(jitter.Next(0, 100)));
        }
    }
}
