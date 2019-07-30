﻿using System;
using System.ComponentModel.DataAnnotations;
using GeoAPI.Geometries;
using HappyTravel.Edo.Api.Infrastructure.Converters;
using Newtonsoft.Json;

namespace HappyTravel.Edo.Api.Models.Locations
{
    [JsonConverter(typeof(GeoPointJsonConverter))]
    public readonly struct GeoPoint : IEquatable<GeoPoint>
    {
        [JsonConstructor]
        public GeoPoint([Range(-180, 180)] double longitude, [Range(-90, 90)] double latitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }


        public GeoPoint(IPoint point)
        {
            Latitude = point.Y;
            Longitude = point.X;
        }


        public double Latitude { get; }

        public double Longitude { get; }


        public override bool Equals(object obj) => obj is GeoPoint point && Equals(point);


        public bool Equals(GeoPoint other) => (Latitude, Longitude) == (other.Latitude, other.Longitude);


        public override int GetHashCode() => (Latitude, Longitude).GetHashCode();


        public static bool operator ==(GeoPoint left, GeoPoint right) => left.Equals(right);


        public static bool operator !=(GeoPoint left, GeoPoint right) => !left.Equals(right);
    }
}