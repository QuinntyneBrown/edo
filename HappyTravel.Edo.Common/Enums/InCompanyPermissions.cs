using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace HappyTravel.Edo.Common.Enums
{
    [JsonConverter(typeof(StringEnumConverter))]
    [Flags]
    public enum InCompanyPermissions
    {
        None = 1,
        EditCompanyInfo = 2,
        PermissionManagementInCompany = 4,
        CustomerInvitation = 8,
        AccommodationAvailabilitySearch = 16,
        AccommodationBooking = 32,
        ViewCompanyAllPaymentHistory = 64,
        PermissionManagementInBranch = 128,
        ObserveMarkupInCompany = 256,
        ObserveMarkupInBranch = 512,
        // "All" permission level should be recalculated after adding new permission
        All = EditCompanyInfo | 
            PermissionManagementInCompany | 
            CustomerInvitation | 
            AccommodationAvailabilitySearch | 
            AccommodationBooking |
            ViewCompanyAllPaymentHistory |
            PermissionManagementInBranch |
            ObserveMarkupInCompany |
            ObserveMarkupInBranch // 1022
    }
}