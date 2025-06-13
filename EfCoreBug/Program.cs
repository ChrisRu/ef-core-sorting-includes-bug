using Microsoft.EntityFrameworkCore;

namespace EfCoreBug;
public static class Program
{
    public static async Task Main(string[] args)
    {
        // TODO: change this to a connection string that works for you.
        const string connectionString = "Server=localhost,1433;Initial Catalog=efcorebug;Persist Security Info=False;User ID=SA;Password=my_password01!;MultipleActiveResultSets=True;Encrypt=False;TrustServerCertificate=False;Connection Timeout=30;";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using (var context = new AppDbContext(options))
        {
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();
            await SeedDataAsync(context);
        }

        await using (var context = new AppDbContext(options))
        {
            await TestIncludeWithVaryingTakeAsync(context);
        }
    }

    private static async Task SeedDataAsync(AppDbContext context)
    {
        Console.WriteLine("Seeding database with 500 services (Dutch and English translations)...");
        for (var i = 1; i <= 500; i++)
        {
            var service = new Service
            {
                // Adding Dutch and English translations, UNUSED is not included!
                Translations = new List<ServiceTranslation>
                {
                    new()
                    {
                        LanguageId = LanguageId.Dutch,
                        Name = $"{Guid.NewGuid()} Dutch Service {i}",
                    },
                    new()
                    {
                        LanguageId = LanguageId.English,
                        Name = $"{Guid.NewGuid()} English Service {i}",
                    }
                },
                Metadatas =
                [
                    new ServiceMetadata
                    {
                        ViewCount = 1
                    },
                ]
            };
            context.Services.Add(service);
        }

        await context.SaveChangesAsync();
        Console.WriteLine("Seeding complete.");
    }

    private static async Task TestIncludeWithVaryingTakeAsync(AppDbContext context)
    {
        Console.WriteLine("\n--- Test: Checking if Translations are Included with Varying Take Values ---");

        var takeValues = Enumerable.Range(1, 20);
        var anyFailures = false;

        foreach (var takeAmount in takeValues)
        {
            Console.WriteLine($"\nTesting with Take({takeAmount}):");
            try
            {
                var results = await context.Services
                    .Include(s => s.Translations)
                    // Only breaks with SplitQuery
                    .AsSplitQuery()
                    // For some reason you need some filtering to trigger the issue
                    .Where(x => x.Metadatas.Any(r => r.ViewCount == 1))
                    .OrderBy(projection =>
                        // HERE IS THE ISSUE:
                        // When changing this sorting to Dutch/English it works fine because it matches the translations.
                        // But when using UNUSED, it fails to include translations.
                        projection.Translations.First(t => t.LanguageId == LanguageId.UNUSED).Name)
                    .Take(takeAmount)
                    .ToListAsync();

                var succeeded = true;
                foreach (var item in results)
                {
                    // Translations are included, so it should be populated.
                    if (item.Translations.Count == 0)
                    {
                        succeeded = false;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(
                            $"  FAIL: Item with Id {item.Id} has EMPTY Translations collection.");
                        Console.ResetColor();
                        break;
                    }
                }

                if (succeeded)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  PASS: Query for Take({takeAmount}) returned {results.Count} items with translations included.");
                }
                else
                {
                    anyFailures = true;
                }

                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  FAIL: Query for Take({takeAmount}) failed with exception: {ex.Message}");
                Console.ResetColor();
                anyFailures = true;
            }
        }

        Console.WriteLine("\n--- Test Summary ---");
        if (anyFailures)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(
                "FAIL: One or more test cases for varying Take() amounts failed to include translations or threw an error.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(
                "PASS: All test cases for varying Take() amounts behaved as expected regarding translation inclusion.");
        }

        Console.ResetColor();
    }
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Service> Services { get; set; } = null!;
    public DbSet<ServiceTranslation> ServiceTranslations { get; set; } = null!;
    public DbSet<ServiceMetadata> ServiceMetadata { get; set; } = null!;
}

public class Service
{
    public int Id { get; set; }
    public ICollection<ServiceTranslation> Translations { get; set; } = null!;
    public ICollection<ServiceMetadata> Metadatas { get; set; } = null!;
}

public class ServiceTranslation
{
    public int Id { get; set; }
    public Service Service { get; set; } = null!;
    public LanguageId LanguageId { get; set; }
    public required string Name { get; set; }
}

public enum LanguageId
{
    Dutch = 1,
    English = 2,
    UNUSED = 3,
}

public class ServiceMetadata
{
    public int Id { get; set; }
    public Service Service { get; set; } = null!;
    public int ViewCount { get; set; }
}

