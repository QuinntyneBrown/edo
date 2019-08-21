﻿using HappyTravel.Edo.Common.Enums;
using System;

namespace HappyTravel.Edo.Api.Infrastructure
{
    public static class ReferenceCodeGenerator
    {
        public static string Generate(ServiceTypes serviceType, string residency, long itineraryNumber)
        {
            //temporary solution 
            //TODO: change
            var randomElement = new Random().Next(10000);

            return string.Join('-', serviceType, residency, itineraryNumber, randomElement);
        }
    }
}