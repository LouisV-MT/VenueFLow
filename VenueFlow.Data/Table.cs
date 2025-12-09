using System;
using System.Collections.Generic;

namespace VenueFlow.Data;

public partial class Table
{
    public int TableId { get; set; }

    public int WeddingId { get; set; }

    public int TableNumber { get; set; }

    public int SeatingCapacity { get; set; }

    public virtual ICollection<Guest> Guests { get; set; } = new List<Guest>();

    public virtual Wedding Wedding { get; set; } = null!;
}
