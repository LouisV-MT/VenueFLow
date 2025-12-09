using System;
using Microsoft.EntityFrameworkCore;
using VenueFlow.Data; // Assuming this is where your DbContext is located
using VenueFlow.Data.Models;
using System.Linq;

namespace VenueFlow.Services
    {
        public class SeatingPlannerService
        {
            private readonly VenueFlowDbContext _context;

            public SeatingPlannerService(VenueFlowDbContext context)
            {
                _context = context;
            }

            // The core method that runs the assignment logic
            public async Task AutoSeatGuests(int weddingId)
            {
                // 1. Fetch all unseated guests and tables for the wedding
                var guests = await _context.Guests
                                           .Where(g => g.WeddingId == weddingId)
                                           .ToListAsync();

                var tables = await _context.Tables
                                           .Where(t => t.WeddingId == weddingId)
                                           .OrderByDescending(t => t.SeatingCapacity)
                                           .ToListAsync();

                var preferences = await _context.SeatingPreferences.ToListAsync();

                // Clear existing assignments for a fresh run
                guests.ForEach(g => g.TableId = null);
                await _context.SaveChangesAsync();


                // --- ALGORITHM PHASES ---

                // Phase 1: Prioritize Hard Constraints (Must Sit With)
                // Group guests by positive constraint pairs (IsMustSitWith == true)
                // (Implementation involves iterating through preferences and grouping guests)

                // Phase 2: Prioritize Family/Group Association
                // (Implementation involves iterating through FamilyGroup and trying to seat them together)

                // Phase 3: Prioritize Optimization (Proximity to Bride)
                // (Implementation involves sorting guests and placing them, always checking against MUST NOT SIT WITH constraints)


                // Save results after all assignments are done
                await _context.SaveChangesAsync();
            }

            // Helper method to check constraints before seating a guest at a table
            private bool CanSitAtTable(int guestId, int tableId, List<SeatingPreference> preferences)
            {
                // Find all guests currently seated at this table
                var seatedGuestIds = _context.Guests.Where(g => g.TableId == tableId).Select(g => g.GuestId).ToList();

                // Check against MUST NOT SIT WITH constraints (IsMustSitWith == false)
                foreach (var seatedId in seatedGuestIds)
                {
                    // Check if the current guest (guestId) has a negative constraint with any seated guest
                    bool hasConflict = preferences.Any(p =>
                        !p.IsMustSitWith &&
                        ((p.GuestIdSource == guestId && p.GuestIdTarget == seatedId) ||
                         (p.GuestIdSource == seatedId && p.GuestIdTarget == guestId))
                    );

                    if (hasConflict)
                    {
                        // Console.WriteLine($"Conflict detected: Guest {guestId} cannot sit with {seatedId} at table {tableId}");
                        return false;
                    }
                }
                return true;
            }

            // --- Additional methods for getting seated guests for the visualization will go here ---
            // public List<SeatViewModel> GetSeatingVisualizationData(int weddingId) { ... }
        }
}
