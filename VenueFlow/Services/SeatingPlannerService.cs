using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VenueFlow.Data.Models;
namespace VenueFlow.Services
{
    public class SeatingPlannerService
    {
        private readonly VenueFlowDbContext _context;

        public SeatingPlannerService(VenueFlowDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Implements the complex greedy seating algorithm based on Proximity and Family Groups.
        /// Prioritizes seating based on ProximityToBride (lowest number first).
        /// </summary>
        public async Task<int> AutoSeatGuests(int weddingId)
        {
            // 1. Fetch all necessary data
            var guests = await _context.Guests
                                       .Where(g => g.WeddingId == weddingId)
                                       .OrderBy(g => g.ProximityToBride) // Sort by proximity (lowest number first)
                                       .ToListAsync();

            var preferences = await _context.SeatingPreferences.ToListAsync();

            // Clear existing assignments for a fresh run
            guests.ForEach(g => g.TableId = null);
            int guestsAssigned = 0;

            // --- Phase 0: Seat Bride and Groom (Proximity 0, Table 0) ---
            var sweetheartTable = await _context.Tables
                                                .FirstOrDefaultAsync(t => t.WeddingId == weddingId && t.TableNumber == 0);

            if (sweetheartTable != null)
            {
                var sweetheartGuests = guests.Where(g => g.ProximityToBride == 0 && g.TableId == null).Take(sweetheartTable.SeatingCapacity).ToList();
                foreach (var guest in sweetheartGuests)
                {
                    guest.TableId = sweetheartTable.TableId;
                    guestsAssigned++;
                }
            }
            // Remove assigned guests from the main processing list
            guests.RemoveAll(g => g.TableId != null);


            // 2. Group remaining guests by ProximityToBride (Lowest number is highest priority)
            var proximityGroups = guests.GroupBy(g => g.ProximityToBride)
                                        .OrderBy(g => g.Key);

            foreach (var proximityGroup in proximityGroups)
            {
                // We only process unseated guests in this proximity group
                var remainingGuestsInProximity = proximityGroup.Where(g => g.TableId == null).ToList();

                // 3. Process guests within the proximity group, prioritizing by FamilyGroup size
                var familyGroups = remainingGuestsInProximity
                    .GroupBy(g => g.FamilyGroup)
                    .OrderByDescending(g => g.Count()); // Largest family group first

                foreach (var familyGroup in familyGroups)
                {
                    var groupMembers = familyGroup.ToList();

                    // a. Check for MUST SIT WITH extension (recursively find all linked guests)
                    var extendedGroup = GetExtendedGroup(groupMembers, guests, preferences);
                    int requiredSize = extendedGroup.Count;

                    // Only process if the entire extended group is still unassigned
                    if (extendedGroup.All(g => g.TableId == null))
                    {
                        // b. Find the first available guest table (lowest TableNumber > 0) that fits the entire group

                        // We refresh the capacity check inside the loop to account for new assignments
                        var tableCapacities = await _context.Tables
                            .Where(t => t.WeddingId == weddingId && t.TableNumber > 0 && t.SeatingCapacity > 2)
                            .Select(t => new
                            {
                                t.TableId,
                                t.TableNumber,
                                t.SeatingCapacity,
                                // Calculate currently seated count
                                CurrentSeated = _context.Guests.Count(g => g.TableId == t.TableId)
                            })
                            .OrderBy(t => t.TableNumber) // Sort by TableNumber to honor proximity (Table 1, then 2, etc.)
                            .ToListAsync();

                        var targetTableInfo = tableCapacities.FirstOrDefault(t =>
                            t.SeatingCapacity - t.CurrentSeated >= requiredSize
                        );

                        if (targetTableInfo != null)
                        {
                            // c. Check for MUST NOT SIT WITH conflicts with already seated guests
                            if (CheckMustNotSitWithConflict(extendedGroup, targetTableInfo.TableId, preferences))
                            {
                                // d. Assign the group to the table
                                foreach (var guest in extendedGroup)
                                {
                                    guest.TableId = targetTableInfo.TableId;
                                    guestsAssigned++;
                                }
                            }
                            // If a conflict exists, the group is skipped for this table, and remains unseated.
                        }
                    }
                }
            }

            // Save results
            await _context.SaveChangesAsync();
            return guestsAssigned;
        }

        /// <summary>
        /// Recursively finds all guests connected by an IsMustSitWith = TRUE constraint.
        /// </summary>
        public List<Guest> GetExtendedGroup(List<Guest> initialGroup, List<Guest> allGuests, List<SeatingPreference> preferences)
        {
            var extendedIds = initialGroup.Select(g => g.GuestId).ToHashSet();
            var queue = new Queue<int>(extendedIds);

            while (queue.Any())
            {
                int currentId = queue.Dequeue();

                // Find all *Must Sit With* partners
                var partners = preferences.Where(p =>
                    p.IsMustSitWith &&
                    (p.GuestIdSource == currentId || p.GuestIdTarget == currentId)
                )
                .Select(p => p.GuestIdSource == currentId ? p.GuestIdTarget : p.GuestIdSource)
                .ToList();

                foreach (var partnerId in partners)
                {
                    if (extendedIds.Add(partnerId)) // Add returns true if the ID was new
                    {
                        queue.Enqueue(partnerId);
                    }
                }
            }
            // Return the actual Guest objects corresponding to the extended IDs
            return allGuests.Where(g => extendedIds.Contains(g.GuestId)).ToList();
        }

        /// <summary>
        /// Checks if the group has any Must Not Sit With conflict with guests already seated at the table.
        /// </summary>
        public bool CheckMustNotSitWithConflict(List<Guest> group, int? tableId, List<SeatingPreference> preferences)
        {
            if (tableId == null) return true;

            // Guests already assigned to the target table
            var seatedGuestIds = _context.Guests
                .Where(g => g.TableId == tableId)
                .Select(g => g.GuestId)
                .ToList();

            // Check every new guest against every seated guest
            foreach (var newGuest in group)
            {
                foreach (var seatedId in seatedGuestIds)
                {
                    // Check for MUST NOT SIT WITH constraints (IsMustSitWith == false)
                    bool hasConflict = preferences.Any(p =>
                        !p.IsMustSitWith &&
                        ((p.GuestIdSource == newGuest.GuestId && p.GuestIdTarget == seatedId) ||
                         (p.GuestIdSource == seatedId && p.GuestIdTarget == newGuest.GuestId))
                    );

                    if (hasConflict)
                    {
                        return false; // Conflict found
                    }
                }
            }
            return true; // No conflicts
        }
    }
}