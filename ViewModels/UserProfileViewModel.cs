namespace AutoSignals.ViewModels
{
    using AutoSignals.Models;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.AspNetCore.Mvc.Rendering;
    using System.Collections.Generic;

    public class UserProfileViewModel
    {
        public IdentityUser User { get; set; }
        public UserData UserData { get; set; }

        public IList<string> Roles { get; set; }
        public IList<SelectListItem> AvailableRoles { get; set; }
        public string SelectedRoleId { get; set; }

        public Exchange Exchange { get; set; }
        public IList<SelectListItem> AvailableExchanges { get; set; }


        public List<Position> Positions { get; set; }
        public int PositionCount { get; set; }
        public int OpenPositionCount { get; set; }

        public List<Order> Orders { get; set; }

        // hold the list of ProviderSettings
        public List<ProviderSettings> ProviderSettings { get; set; }
    }
}
