using System;
using System.Collections.Generic;

namespace VenueFlow.Models;

public partial class MenuOption
{
    public int MenuOptionId { get; set; }

    public string OptionName { get; set; } = null!;

    public string? Category { get; set; }

    public string? AlergyInfo { get; set; }

    public virtual ICollection<Guest> Guests { get; set; } = new List<Guest>();
}
