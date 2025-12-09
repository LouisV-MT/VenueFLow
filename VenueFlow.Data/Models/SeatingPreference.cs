using System;
using System.Collections.Generic;

namespace VenueFlow.Data.Models;

public partial class SeatingPreference
{
    public int PreferenceId { get; set; }

    public int GuestIdSource { get; set; }

    public int GuestIdTarget { get; set; }

    // RENAME: Changed from 'Type' (NVARCHAR) to 'IsMustSitWith' (bool/BIT)
    // TRUE = Must Sit With; FALSE = Must Not Sit With
    public bool IsMustSitWith { get; set; }

    public virtual Guest GuestIdSourceNavigation { get; set; } = null!;

    public virtual Guest GuestIdTargetNavigation { get; set; } = null!;
}
