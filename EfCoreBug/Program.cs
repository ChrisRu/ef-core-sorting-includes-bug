using Microsoft.EntityFrameworkCore;

namespace EfCoreBug;

public enum LanguageId
{
    Dutch = 1,
    English = 2,
    UNUSED = 3,
}

public class Service
{
    public int Id { get; set; }
    public ICollection<ServiceTranslation> Translations { get; set; } = new List<ServiceTranslation>();
    public ICollection<ServiceMetadata> Metadatas { get; set; } = new List<ServiceMetadata>();
}

public class ServiceMetadata
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

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Service> Services { get; set; } = null!;
    public DbSet<ServiceTranslation> ServiceTranslations { get; set; } = null!;
    public DbSet<ServiceMetadata> ServiceMetadata { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Service>()
            .HasMany(s => s.Translations)
            .WithOne(st => st.Service)
            .HasForeignKey(st => st.ServiceId);

        modelBuilder.Entity<ServiceTranslation>()
            .HasKey(st => new { EntityId = st.ServiceId, st.LanguageId });

        modelBuilder.Entity<ServiceMetadata>()
            .HasOne(x => x.Service)
            .WithMany(x => x.Metadatas)
            .HasForeignKey(x => x.ServiceId);
    }
}

public static class Program
{
    private static readonly Random Random = new(100);

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

    private static async Task SeedDataAsync(AppDbContext context)
    {
        Console.WriteLine("Seeding database with 500 services (Dutch and English translations)...");
        for (var i = 1; i <= 500; i++)
        {
            var service = new Service
            {
                Translations = new List<ServiceTranslation>
                {
                    // Adding Dutch and English translations, UNUSED is not included!
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
                Metadatas = Enumerable.Range(0, 2)
                    .Select(_ => new ServiceMetadata
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

        var takeValues = Enumerable.Range(1, 20);
        var anyFailures = false;

        foreach (var takeAmount in takeValues)
        {
            Console.WriteLine($"\nTesting with Take({takeAmount}):");
            try
            {
                var results = await context.Services
                    .Include(s => s.Translations)
                    .AsSplitQuery()
                    .Where(x => x.Metadatas.Any(r => r.ViewCount == 1))
                    .OrderBy(projection =>
                        projection.Translations
                            .First(t => t.LanguageId == LanguageId.UNUSED).Name)
                    .Take(takeAmount)
                    .ToListAsync();

                foreach (var item in results)
                {
                    if (item.Translations.Count == 0)
                    {
                        anyFailures = true;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(
                            $"  FAIL: Item with Id {item.Id} has EMPTY Translations collection.");
                        Console.ResetColor();
                        break;
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
}