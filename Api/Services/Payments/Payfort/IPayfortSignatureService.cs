﻿using System.Collections.Generic;
using CSharpFunctionalExtensions;
using HappyTravel.Edo.Api.Models.Payments.Payfort;
using HappyTravel.Edo.Common.Enums;
using Newtonsoft.Json.Linq;

namespace HappyTravel.Edo.Api.Services.Payments.Payfort
{
    public interface IPayfortSignatureService
    {
        Result<string> Calculate(JObject model, SignatureTypes type);

        Result<string> Calculate(IDictionary<string, string> models, SignatureTypes type);

        Result<T> Check<T>(JObject response, T model) where T : ISignedResponse;
    }
}