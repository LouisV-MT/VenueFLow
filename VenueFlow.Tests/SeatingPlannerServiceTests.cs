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
            var preferences = new List<SeatingPreference>(); // Empty list

            var initialGroup = new List<Guest> { guest1 };

            var result = SeatingPlannerService.GetExtendedGroup(initialGroup, allGuests, preferences);
            
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, result[0].GuestId);
        }
    }
}