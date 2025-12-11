using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using VenueFlow.Data.Models;
using VenueFlow.Services;

namespace VenueFlow.Tests.Services
{
    [TestClass]
    public class SeatingPlannerServiceTests
    {
        [TestMethod]
        public void GetExtendedGroup_ReturnsAllLinkedGuests()
        {
            // Arrange
            var guest1 = new Guest { GuestId = 1, GuestName = "A" };
            var guest2 = new Guest { GuestId = 2, GuestName = "B" };
            var guest3 = new Guest { GuestId = 3, GuestName = "C" };
            var guest4 = new Guest { GuestId = 4, GuestName = "D" }; // Not linked

            var allGuests = new List<Guest> { guest1, guest2, guest3, guest4 };

            var preferences = new List<SeatingPreference>
            {
                // A must sit with B
                new SeatingPreference { GuestIdSource = 1, GuestIdTarget = 2, IsMustSitWith = true },
                // B must sit with C
                new SeatingPreference { GuestIdSource = 2, GuestIdTarget = 3, IsMustSitWith = true }
            };

            var initialGroup = new List<Guest> { guest1 };

            // Act
            // If SeatingPlannerService is not found, ensure VenueFlow.Tests.csproj targets 'net-windows'
            var result = SeatingPlannerService.GetExtendedGroup(initialGroup, allGuests, preferences);

            // Assert
            Assert.AreEqual(3, result.Count, "Should return 3 guests (A, B, and C)");
            Assert.IsTrue(result.Any(g => g.GuestId == 1));
            Assert.IsTrue(result.Any(g => g.GuestId == 2));
            Assert.IsTrue(result.Any(g => g.GuestId == 3));
            Assert.IsFalse(result.Any(g => g.GuestId == 4));
        }

        [TestMethod]
        public void GetExtendedGroup_ReturnsOnlyInitialGuest_WhenNoPreferencesExist()
        {            
            var guest1 = new Guest { GuestId = 1, GuestName = "A" };
            var guest2 = new Guest { GuestId = 2, GuestName = "B" };
            var allGuests = new List<Guest> { guest1, guest2 };
            var preferences = new List<SeatingPreference>();

            var initialGroup = new List<Guest> { guest1 };

            var result = SeatingPlannerService.GetExtendedGroup(initialGroup, allGuests, preferences);
            
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, result[0].GuestId);
        }

        [TestMethod]
        public void GetExtendedGroup_HandlesCircularReferences_WithoutCrashing()
        {
            var g1 = new Guest { GuestId = 1 };
            var g2 = new Guest { GuestId = 2 };
            var allGuests = new List<Guest> { g1, g2 };

            var prefs = new List<SeatingPreference>
            {
                new SeatingPreference { GuestIdSource = 1, GuestIdTarget = 2, IsMustSitWith = true },
                new SeatingPreference { GuestIdSource = 2, GuestIdTarget = 1, IsMustSitWith = true }
            };

            var initialGroup = new List<Guest> { g1 };

            var result = SeatingPlannerService.GetExtendedGroup(initialGroup, allGuests, prefs);

            Assert.AreEqual(2, result.Count, "Should handle circular links and return both guests.");
        }

        [TestMethod]
        public void CheckConflict_ReturnsTrue_WhenOnlyPositivePreferencesExist()
        {
            var service = new SeatingPlannerService(null);
            var g1 = new Guest { GuestId = 1, TableId = 5 };
            var g2 = new Guest { GuestId = 2 };
            var allGuests = new List<Guest> { g1, g2 };

            var prefs = new List<SeatingPreference>
            {
                new SeatingPreference { GuestIdSource = 1, GuestIdTarget = 2, IsMustSitWith = true }
            };

            var group = new List<Guest> { g2 };

            
            bool isSafe = service.CheckMustNotSitWithConflict(group, 5, prefs, allGuests);

            Assert.IsTrue(isSafe, "Positive preferences should not trigger a conflict warning.");
        }

        // Test 5: Conflict Logic - Empty Table (Boundary Test)
        [TestMethod]
        public void CheckConflict_ReturnsTrue_WhenTableIsEmpty()
        {
            var service = new SeatingPlannerService(null);
            var newGuest = new Guest { GuestId = 1 };
            var allGuests = new List<Guest> { newGuest };
            var prefs = new List<SeatingPreference>();
            var group = new List<Guest> { newGuest };

            bool isSafe = service.CheckMustNotSitWithConflict(group, 99, prefs, allGuests);

            Assert.IsTrue(isSafe, "Empty table should never have a conflict.");
        }

        // Test 6: Math Logic - Table Coordinates
        [TestMethod]
        public void TableCoordinates_Radius_CalculatesHalfWidth()
        {
            double width = 200;
            double height = 200;

            var table = new TableCoordinates(1, 1, 100, 100, width, height, 10);

            Assert.AreEqual(100, table.Radius, "Radius should be exactly half of width.");
        }

        // Test 7: Data Logic - Guest Defaults
        [TestMethod]
        public void Guest_IsUnseated_ByDefault()
        {
            var guest = new Guest();

            Assert.IsNull(guest.TableId, "New guest should not be assigned a table automatically.");
        }
    }
}