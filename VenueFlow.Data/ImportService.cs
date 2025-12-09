using MiniExcelLibs;
using System;
using System.Linq;
using System.Collections.Generic;
using VenueFlow.Data.Models; 

namespace VenueFlow.Data
{
    public class ImportService
    {
        private class ExcelRow
        {
            public string? FullName { get; set; }
            public string? FamilyGroup { get; set; }
            public string? MealChoice { get; set; }
            public string? Allergies { get; set; }
            public int Proximity { get; set; } 
        }

        public void ImportWedding(string filePath)
        {
            using (var context = new VenueFlowDbContext())
            {
               
                var rows = MiniExcel.Query<ExcelRow>(filePath).ToList();
                if (!rows.Any()) return;

                var couple = rows
                    .Where(r => r.Proximity == 0 && !string.IsNullOrEmpty(r.FullName))
                    .Select(r => r.FullName)
                    .ToList();

                string weddingName = "Wedding Event";
                if (couple.Count > 0)
                {
                    weddingName = $"Wedding of {string.Join(" & ", couple)}";
                }

                
                var newWedding = new Wedding
                {
                    Name = weddingName,
                    Date = DateOnly.FromDateTime(DateTime.Now.AddMonths(1)), 
                    RoomCapacity = rows.Count + 10    
                };
                context.Weddings.Add(newWedding);
                context.SaveChanges();

                var distinctMeals = rows
                    .Select(r => r.MealChoice)
                    .Where(m => !string.IsNullOrEmpty(m))
                    .Distinct();

                var dbMeals = context.MenuOptions.ToList();

                foreach (var mealName in distinctMeals)
                {
                    if (mealName != null && !dbMeals.Any(m => m.OptionName == mealName))
                    {
                        var newMenu = new MenuOption { OptionName = mealName, Category = "Standard", AlergyInfo = "" };
                        context.MenuOptions.Add(newMenu);
                        context.SaveChanges();
                        dbMeals.Add(newMenu);
                    }
                }

                foreach (var row in rows)
                {
                    if (string.IsNullOrEmpty(row.FullName)) continue;

                    var mealId = dbMeals.FirstOrDefault(m => m.OptionName == row.MealChoice)?.MenuOptionId;

                    var guest = new Guest
                    {
                        WeddingId = newWedding.WeddingId, 
                        GuestName = row.FullName,
                        FamilyGroup = row.FamilyGroup,
                        MenuOptionId = mealId,
                        Allergies = row.Allergies ?? "None",
                        ProximityToBride = row.Proximity,
                        TableId = null
                    };
                    context.Guests.Add(guest);
                }

                context.SaveChanges();
            }
        }
    }
}