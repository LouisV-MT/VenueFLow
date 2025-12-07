using System;
using System.Collections.Generic;

namespace VenueFlow.Models;

public partial class Guest
{
    public int GuestId { get; set; }

    public int WeddingId { get; set; }

    public int? TableId { get; set; }

    public int? MenuOptionId { get; set; }

    public string GuestName { get; set; } = null!;

    public string? FamilyGroup { get; set; }

    public int? ProximityToBride { get; set; }

    public string? DietaryRestrictions { get; set; }

    public string? Allergies { get; set; }

    public virtual MenuOption? MenuOption { get; set; }

    public virtual ICollection<SeatingPreference> SeatingPreferenceGuestIdSourceNavigations { get; set; } = new List<SeatingPreference>();

    public virtual ICollection<SeatingPreference> SeatingPreferenceGuestIdTargetNavigations { get; set; } = new List<SeatingPreference>();

    public virtual Table? Table { get; set; }

    public virtual Wedding Wedding { get; set; } = null!;
}
