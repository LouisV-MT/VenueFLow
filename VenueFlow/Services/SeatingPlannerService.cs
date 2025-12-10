using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using VenueFlow.Data;
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

        public async Task<int> AutoSeatGuests(int weddingId)
        {
            // 1. Fetch ALL data needed into memory first
            var guests = await _context.Guests
                                       .Where(g => g.WeddingId == weddingId)
                                       .OrderBy(g => g.ProximityToBride)
                                       .ToListAsync();

            var tables = await _context.Tables
                                       .Where(t => t.WeddingId == weddingId)
                                       .OrderBy(t => t.TableNumber)
                                       .ToListAsync();

            var preferences = await _context.SeatingPreferences.ToListAsync();

            // Clear assignments in memory
            guests.ForEach(g => g.TableId = null);
            int guestsAssigned = 0;

            // Track seated counts locally to avoid DB calls in loop
            var seatedCounts = tables.ToDictionary(t => t.TableId, t => 0);

            // --- Phase 0: Seat Bride and Groom ---
            var sweetheartTable = tables.FirstOrDefault(t => t.TableNumber == 0);
            if (sweetheartTable != null)
            {
                var sweetheartGuests = guests.Where(g => g.ProximityToBride == 0).Take(sweetheartTable.SeatingCapacity).ToList();
                foreach (var guest in sweetheartGuests)
                {
                    guest.TableId = sweetheartTable.TableId;
                    guestsAssigned++;
                    seatedCounts[sweetheartTable.TableId]++;
                }
            }
            // Filter out assigned guests for next steps
            var unassignedGuests = guests.Where(g => g.TableId == null).ToList();

            // 2. Group by Proximity
            var proximityGroups = unassignedGuests.GroupBy(g => g.ProximityToBride).OrderBy(g => g.Key);

            foreach (var proximityGroup in proximityGroups)
            {
                // 3. Process groups, prioritizing largest first
                var familyGroups = proximityGroup
                    .Where(g => g.TableId == null)
                    .GroupBy(g => g.FamilyGroup)
                    .OrderByDescending(g => g.Count());

                foreach (var familyGroup in familyGroups)
                {
                    var groupMembers = familyGroup.ToList();

                    // a. Check for MUST SIT WITH extension
                    var extendedGroup = GetExtendedGroup(groupMembers, guests, preferences);
                    int requiredSize = extendedGroup.Count;

                    if (extendedGroup.All(g => g.TableId == null))
                    {
                        // b. Find first available table using local data
                        var targetTable = tables
                            .Where(t => t.TableNumber > 0 && t.SeatingCapacity > 2)
                            .OrderBy(t => t.TableNumber)
                            .FirstOrDefault(t => t.SeatingCapacity - seatedCounts[t.TableId] >= requiredSize);

                        if (targetTable != null)
                        {
                            // c. Check conflicts using in-memory list
                            if (CheckMustNotSitWithConflict(extendedGroup, targetTable.TableId, preferences, guests))
                            {
                                // d. Assign
                                foreach (var guest in extendedGroup)
                                {
                                    guest.TableId = targetTable.TableId;
                                    guestsAssigned++;
                                }
                                seatedCounts[targetTable.TableId] += requiredSize;
                            }
                        }
                    }
                }
            }

            // Save all changes at once
            await _context.SaveChangesAsync();
            return guestsAssigned;
        }

        public static List<Guest> GetExtendedGroup(List<Guest> initialGroup, List<Guest> allGuests, List<SeatingPreference> preferences)
        {
            var extendedIds = initialGroup.Select(g => g.GuestId).ToHashSet();
            var queue = new Queue<int>(extendedIds);

            while (queue.Count > 0)
            {
                int currentId = queue.Dequeue();
                var partners = preferences.Where(p =>
                    p.IsMustSitWith &&
                    (p.GuestIdSource == currentId || p.GuestIdTarget == currentId)
                ).Select(p => p.GuestIdSource == currentId ? p.GuestIdTarget : p.GuestIdSource).ToList();

                foreach (var partnerId in partners)
                {
                    if (extendedIds.Add(partnerId)) queue.Enqueue(partnerId);
                }
            }
            return allGuests.Where(g => extendedIds.Contains(g.GuestId)).ToList();
        }

        // UPDATED SIGNATURE: Accepts 'allGuests' to check conflicts in memory
        public bool CheckMustNotSitWithConflict(List<Guest> group, int? tableId, List<SeatingPreference> preferences, List<Guest> allGuests)
        {
            if (tableId == null) return true;

            var seatedGuestIds = allGuests
                .Where(g => g.TableId == tableId && !group.Contains(g))
                .Select(g => g.GuestId)
                .ToList();

            foreach (var newGuest in group)
            {
                foreach (var seatedId in seatedGuestIds)
                {
                    bool hasConflict = preferences.Any(p =>
                        !p.IsMustSitWith &&
                        ((p.GuestIdSource == newGuest.GuestId && p.GuestIdTarget == seatedId) ||
                         (p.GuestIdSource == seatedId && p.GuestIdTarget == newGuest.GuestId))
                    );
                    if (hasConflict) return false;
                }
            }
            return true;
        }
    }
}