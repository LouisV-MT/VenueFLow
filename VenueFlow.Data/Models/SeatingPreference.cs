using System;
using System.Collections.Generic;

namespace VenueFlow.Data.Models;

public partial class SeatingPreference
{
    public int PreferenceId { get; set; }

    public int GuestIdSource { get; set; }

    public int GuestIdTarget { get; set; }

    public string Type { get; set; } = null!;

    public virtual Guest GuestIdSourceNavigation { get; set; } = null!;

    public virtual Guest GuestIdTargetNavigation { get; set; } = null!;
}
