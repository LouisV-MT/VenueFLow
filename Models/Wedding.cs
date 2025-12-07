using System;
using System.Collections.Generic;

namespace VenueFlow.Models;

public partial class Wedding
{
    public int WeddingId { get; set; }

    public string Name { get; set; } = null!;

    public DateOnly Date { get; set; }

    public int RoomCapacity { get; set; }

    public virtual ICollection<Guest> Guests { get; set; } = new List<Guest>();

    public virtual ICollection<Table> Tables { get; set; } = new List<Table>();
}
