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
    public int EntityId { get; set; }
    public Service Service { get; set; } = null!;

    public LanguageId LanguageId { get; set; }
    public required string Name { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Service> Services { get; set; } = null!;
    public DbSet<ServiceTranslation> ServiceTranslations { get; set; } = null!;
    public DbSet<RandomStuff> RandomStuffs { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Service>()
            .HasMany(s => s.Translations)
            .WithOne(st => st.Service)
            .HasForeignKey(st => st.EntityId);

        modelBuilder.Entity<ServiceTranslation>()
            .HasKey(st => new { st.EntityId, st.LanguageId });

        modelBuilder.Entity<RandomStuff>()
            .HasOne(x => x.Service)
            .WithMany(x => x.Random)
            .HasForeignKey(x => x.ServiceId);
    }
}

public static class Program
{
    private static readonly Random Random = new(100);

    private static async Task SeedDataAsync(AppDbContext context)
    {
        // Using AnyAsync on a specific table within the schema is more reliable
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
                    // Adding Dutch and English translations, German is not included!
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
                Random = Enumerable.Range(0, Random.Next(1, 5))
                    .Select(_ => new RandomStuff
                    {
                        ViewCount = Random.Next(0, 2) == 0 ? 0 : 1,
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
                    .Where(x => x.Random.Any(r => r.ViewCount != 1))
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

                if (results.Count == 0 && takeAmount is > 0 and <= 100)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  WARN: Take({takeAmount}) returned 0 items, but data should exist.");
                    Console.ResetColor();
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
        const string connectionString = "Server=localhost,1433;Initial Catalog=efcorebug;Persist Security Info=False;User ID=SA;Password=my_password01!;MultipleActiveResultSets=True;Encrypt=False;TrustServerCertificate=False;Connection Timeout=30;";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        Console.WriteLine("Ensuring database is clean before test...");
        await using (var context = new AppDbContext(options))
        {
            await context.Database.EnsureDeletedAsync();
            Console.WriteLine("Database dropped.");

            await context.Database.EnsureCreatedAsync();
            Console.WriteLine("Database and schema 'efcore_bug_repro' created.");

            await SeedDataAsync(context);
        }

        await using (var context = new AppDbContext(options))
        {
            await TestIncludeWithVaryingTakeAsync(context);
        }

        Console.WriteLine("\nReproduction finished. Check console output for results.");
    }
}