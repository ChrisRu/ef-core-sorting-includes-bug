using Microsoft.EntityFrameworkCore;

namespace EfCoreBug;

public enum LanguageId
{
    Dutch = 1,
    English = 2,
    German = 3,
}

public class Service
{
    public int Id { get; set; }
    public ICollection<ServiceTranslation> Translations { get; set; } = new List<ServiceTranslation>();
    public ICollection<RandomStuff> Random { get; set; } = new List<RandomStuff>();
}

public class RandomStuff
{
    public int Id { get; set; }
    public int ViewCount { get; set; }
    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;
}

public class ServiceTranslation
{
    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;

    public LanguageId LanguageId { get; set; }
    public required string Name { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Service> Services { get; set; } = null!;
    public DbSet<ServiceTranslation> ServiceTranslations { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Service>()
            .HasMany(s => s.Translations)
            .WithOne(st => st.Service)
            .HasForeignKey(st => st.ServiceId);

        modelBuilder.Entity<ServiceTranslation>()
            .HasKey(st => new { st.ServiceId, st.LanguageId });

        modelBuilder.Entity<RandomStuff>()
            .HasOne(x => x.Service)
            .WithMany(x => x.Random)
            .HasForeignKey(x => x.ServiceId);
    }
}

public static class Program
{
    private static async Task SeedDataAsync(AppDbContext context)
    {
        if (await context.Services.AnyAsync())
        {
            Console.WriteLine("Database already seeded.");
            return;
        }

        Console.WriteLine("Seeding database with 500 services (Dutch and English translations)...");
        for (var i = 1; i <= 500; i++)
        {
            var service = new Service
            {
                Translations = new List<ServiceTranslation>
                {
                    new()
                    {
                        LanguageId = LanguageId.Dutch,
                        Name = $"Dutch Service {i}",
                    },
                    new()
                    {
                        LanguageId = LanguageId.English,
                        Name = $"English Service {i}",
                    }
                },
                Random = Enumerable.Range(0, 5)
                    .Select(index => new RandomStuff
                    {
                        ViewCount = index * 10,
                    })
                    .ToList()
            };
            context.Services.Add(service);
        }

        await context.SaveChangesAsync();
        Console.WriteLine("Seeding complete.");
    }

    private static async Task TestIncludeWithVaryingTakeAsync(AppDbContext context)
    {
        Console.WriteLine("\n--- Test: Checking if Translations are Included with Varying Take Values ---");

        var takeValues = Enumerable.Range(1, 100);
        var anyFailures = false;

        foreach (var takeAmount in takeValues)
        {
            Console.WriteLine($"\nTesting with Take({takeAmount}):");
            try
            {
                var results = await context.Services
                    .Include(s => s.Translations)
                    .AsSplitQuery()
                    .OrderBy(projection =>
                        projection.Translations
                            .First(t => t.LanguageId == LanguageId.German).Name)
                    .Select(s => new { ServiceEntity = s, CalculatedY = s.Random.Count })
                    .Skip(0)
                    .Take(takeAmount)
                    .ToListAsync();

                Console.WriteLine($"  Query returned {results.Count} items.");

                if (takeAmount == 0)
                {
                    if (results.Any())
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  FAIL: Take(0) returned {results.Count} items, expected 0.");
                        anyFailures = true;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("  PASS: Take(0) returned 0 items as expected.");
                    }

                    Console.ResetColor();
                    continue;
                }

                if (results.Count == 0 && takeAmount is > 0 and <= 100) // 100 is max items we have
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  WARN: Take({takeAmount}) returned 0 items, but data should exist.");
                    Console.ResetColor();
                    // This could be a symptom or a different issue.
                    // For the purpose of this test, we'll focus on whether loaded items have translations.
                }

                bool allIncludedProperly = true;
                if (results.Any())
                {
                    foreach (var item in results)
                    {
                        if (item.ServiceEntity.Translations == null || item.ServiceEntity.Translations.Count == 0)
                        {
                            allIncludedProperly = false;
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine(
                                $"  FAIL: Item with Id {item.ServiceEntity.Id} has NULL or EMPTY Translations collection.");
                            Console.ResetColor();
                            break;
                        }

                        if (item.ServiceEntity.Translations.Count != 2)
                        {
                            allIncludedProperly = false;
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine(
                                $"  FAIL: Item with Id {item.ServiceEntity.Id} has {item.ServiceEntity.Translations.Count} translations, expected 2.");
                            Console.ResetColor();
                            break;
                        }
                    }

                    if (allIncludedProperly)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"  PASS: All {results.Count} items have their Translations included.");
                    }
                    else
                    {
                        anyFailures = true;
                    }
                }
                else if (takeAmount is > 0 and <= 100)
                {
                    // If no results but expected, it's a different kind of failure, but not an include failure per se.
                    // For this specific test, if results.Any() is false, the loop for checking includes won't run.
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


    public static async Task Main(string[] args)
    {
        const string dbFile = "efcore_repro.db";
        const string connectionString = $"Data Source={dbFile}";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options;

        if (File.Exists(dbFile))
        {
            File.Delete(dbFile);
        }

        await using (var context = new AppDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
            await SeedDataAsync(context);
        }

        await using (var context = new AppDbContext(options))
        {
            await TestIncludeWithVaryingTakeAsync(context);
        }

        Console.WriteLine($"\nDatabase file created at: {Path.GetFullPath(dbFile)}");
        Console.WriteLine("Reproduction finished. Check console output for results.");
    }
}