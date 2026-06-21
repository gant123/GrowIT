using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GrowIT.Backend.Services;

/// <summary>
/// Seeds a realistic multi-tenant demo dataset: ~3 organizations with staff, clients,
/// households, funds, programs, service investments, outcomes (imprints), growth plans,
/// tasks, and beta feedback. demontegant@gmail.com is provisioned as the platform
/// SuperAdmin and owns the first (richly populated) organization.
///
/// Idempotent: re-running skips organizations that already have demo data, and never
/// resets an existing user's password.
///
/// Invoke with: dotnet run --project src/GrowIT.Client -- --seed-demo
/// </summary>
public sealed class DemoDataSeeder
{
    public const string DemoPassword = "GrowIT!Demo2026";
    public const string SuperAdminEmail = "demontegant@gmail.com";

    private const string SeedMarkerFundName = "General Assistance Fund";

    private static readonly string[] FirstNames =
        { "James", "Maria", "David", "Aisha", "Robert", "Linda", "Carlos", "Fatima",
          "Michael", "Sandra", "Wei", "Grace", "Tyler", "Nia", "Omar", "Elena", "Jamal", "Sofia" };

    private static readonly string[] LastNames =
        { "Johnson", "Garcia", "Nguyen", "Patel", "Williams", "Brown", "Okafor", "Rodriguez",
          "Lee", "Martinez", "Davis", "Khan", "Thompson", "Hernandez", "Ali", "Cohen", "Reyes", "Bauer" };

    private static readonly (string Name, decimal UnitCost, string Period)[] ProgramCatalog =
    {
        ("Rent Assistance", 850m, "Monthly"),
        ("Food Security", 120m, "Weekly"),
        ("Job Training", 600m, "Annual"),
        ("Utility Relief", 200m, "Monthly"),
    };

    private static readonly string[] ImprintTitles =
        { "Secured stable housing", "Completed job training", "Child made honor roll",
          "Paid off outstanding debt", "Gained full-time employment", "Enrolled in healthcare coverage",
          "Opened first savings account", "Reconnected utilities" };

    private static readonly string[] GrowthPlanTitles =
        { "Path to Self-Sufficiency", "Housing Stability Plan", "Career Development Plan", "Family Wellbeing Plan" };

    private readonly ApplicationDbContext _db;
    private readonly UserManager<User> _users;
    private readonly RoleManager<IdentityRole<Guid>> _roles;
    private readonly ILogger _log;
    private readonly Random _rng = new(20260621);

    public DemoDataSeeder(
        ApplicationDbContext db,
        UserManager<User> users,
        RoleManager<IdentityRole<Guid>> roles,
        ILogger log)
    {
        _db = db;
        _users = users;
        _roles = roles;
        _log = log;
    }

    public async Task SeedAsync()
    {
        foreach (var role in new[] { "SuperAdmin", "Owner", "Admin", "Manager", "Case Manager", "Analyst", "Member" })
        {
            await EnsureRoleAsync(role);
        }

        // --- Organization 1: owned by the SuperAdmin (demontegant) ---
        var superAdmin = await EnsureSuperAdminAsync();
        var org1 = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == superAdmin.TenantId);
        if (org1 is null)
        {
            org1 = NewTenant("Hope Harbor Community Services", "Community Services", "11-50");
            _db.Tenants.Add(org1);
            await _db.SaveChangesAsync();
            superAdmin.TenantId = org1.Id;
            await _users.UpdateAsync(superAdmin);
        }

        var org1Staff = new List<User>
        {
            superAdmin,
            await EnsureUserAsync("admin@hopeharbor.org", "Hannah", "Brooks", "Admin", org1.Id),
            await EnsureUserAsync("manager@hopeharbor.org", "Marcus", "Webb", "Manager", org1.Id),
            await EnsureUserAsync("casework@hopeharbor.org", "Priya", "Shah", "Case Manager", org1.Id),
        };
        await SeedTenantDataAsync(org1, org1Staff);

        // --- Organizations 2 & 3: independent tenants with their own staff ---
        await SeedOrganizationAsync("Riverside Family Network", "Family Services", "1-10", "riverside.org");
        await SeedOrganizationAsync("Bright Futures Collective", "Youth & Education", "51-200", "brightfutures.org");

        _log.LogInformation(
            "Demo seed complete. SuperAdmin: {Email}. All seeded staff log in with password: {Password}",
            SuperAdminEmail, DemoPassword);
    }

    private async Task SeedOrganizationAsync(string name, string type, string size, string domain)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Name == name);
        if (tenant is null)
        {
            tenant = NewTenant(name, type, size);
            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync();
        }

        var staff = new List<User>
        {
            await EnsureUserAsync($"owner@{domain}", RandomFirst(), RandomLast(), "Owner", tenant.Id),
            await EnsureUserAsync($"manager@{domain}", RandomFirst(), RandomLast(), "Manager", tenant.Id),
            await EnsureUserAsync($"casework@{domain}", RandomFirst(), RandomLast(), "Case Manager", tenant.Id),
        };
        await SeedTenantDataAsync(tenant, staff);
    }

    private async Task SeedTenantDataAsync(Tenant tenant, List<User> staff)
    {
        var alreadySeeded = await _db.Funds.IgnoreQueryFilters()
            .AnyAsync(f => f.TenantId == tenant.Id && f.Name == SeedMarkerFundName);
        if (alreadySeeded)
        {
            _log.LogInformation("Org '{Name}' already has demo data; skipping.", tenant.Name);
            return;
        }

        _log.LogInformation("Seeding demo data for org '{Name}'...", tenant.Name);
        var staffIds = staff.Select(s => s.Id).ToList();

        // Funds + programs (no dependencies)
        var generalFund = new Fund { TenantId = tenant.Id, Name = SeedMarkerFundName, TotalAmount = 75000m, AvailableAmount = 75000m };
        var emergencyFund = new Fund { TenantId = tenant.Id, Name = "Emergency Relief Fund", TotalAmount = 30000m, AvailableAmount = 30000m };
        _db.Funds.AddRange(generalFund, emergencyFund);

        var programs = ProgramCatalog.Select(p => new Program
        {
            TenantId = tenant.Id,
            Name = p.Name,
            Description = $"{p.Name} support for households working toward stability.",
            DefaultUnitCost = p.UnitCost,
            CapacityLimit = _rng.Next(20, 80),
            CapacityPeriod = p.Period,
        }).ToList();
        _db.Programs.AddRange(programs);
        await _db.SaveChangesAsync();

        // Save in dependency order — Household <-> Client is circular and FamilyMember
        // has no navigation to Client, so EF can't auto-order these in one batch.
        var clients = new List<Client>();
        var householdHeads = new List<(Household Household, Client Head)>();
        var familyMembers = new List<FamilyMember>();
        var householdCount = _rng.Next(4, 7);
        for (var h = 0; h < householdCount; h++)
        {
            var lastName = RandomLast();
            var household = new Household { TenantId = tenant.Id, Name = $"The {lastName} Family" };
            var head = NewClient(tenant.Id, RandomFirst(), lastName, HouseholdRole.Head);
            head.HouseholdId = household.Id;

            var dependents = _rng.Next(0, 4);
            head.HouseholdCount = dependents + 1;
            for (var d = 0; d < dependents; d++)
            {
                familyMembers.Add(new FamilyMember
                {
                    TenantId = tenant.Id,
                    ClientId = head.Id,
                    FirstName = RandomFirst(),
                    LastName = lastName,
                    Relationship = d == 0 && head.MaritalStatus == MaritalStatus.Married ? "Spouse" : "Child",
                    DateOfBirth = Utc(DateTime.UtcNow.AddYears(-_rng.Next(2, 17))),
                    SchoolOrEmployer = "Lincoln Elementary",
                    Notes = "Added during intake.",
                });
            }

            householdHeads.Add((household, head));
            clients.Add(head);
        }

        // A few standalone clients (no household)
        for (var s = 0; s < _rng.Next(2, 5); s++)
        {
            clients.Add(NewClient(tenant.Id, RandomFirst(), RandomLast(), HouseholdRole.Other));
        }

        _db.Households.AddRange(householdHeads.Select(x => x.Household));
        await _db.SaveChangesAsync();                       // 1) households exist

        _db.Clients.AddRange(clients);
        await _db.SaveChangesAsync();                       // 2) clients exist (HouseholdId valid)

        foreach (var (household, head) in householdHeads)
        {
            household.PrimaryClientId = head.Id;
        }
        _db.FamilyMembers.AddRange(familyMembers);
        await _db.SaveChangesAsync();                       // 3) link households + family members

        // Investments (seeds): mix of statuses, drawn from funds + programs
        var statuses = new[] { InvestmentStatus.Pending, InvestmentStatus.Approved, InvestmentStatus.Disbursed, InvestmentStatus.Completed };
        var investments = new List<Investment>();
        var investmentCount = clients.Count * 2;
        for (var i = 0; i < investmentCount; i++)
        {
            var client = clients[_rng.Next(clients.Count)];
            var program = programs[_rng.Next(programs.Count)];
            var fund = _rng.Next(2) == 0 ? generalFund : emergencyFund;
            var units = _rng.Next(1, 4);
            var amount = program.DefaultUnitCost * units;

            investments.Add(new Investment
            {
                TenantId = tenant.Id,
                ClientId = client.Id,
                FundId = fund.Id,
                ProgramId = program.Id,
                Amount = amount,
                SnapshotUnitCost = program.DefaultUnitCost,
                PayeeName = $"{program.Name} Provider",
                Reason = $"{program.Name} for the {client.LastName} household.",
                CreatedBy = staffIds[_rng.Next(staffIds.Count)],
                CreatedAt = Utc(DateTime.UtcNow.AddDays(-_rng.Next(1, 180))),
                Status = statuses[_rng.Next(statuses.Length)],
            });

            if (fund.AvailableAmount >= amount)
            {
                fund.AvailableAmount -= amount;
            }
        }
        _db.Investments.AddRange(investments);

        // Imprints (outcomes / milestones)
        var imprintCategories = Enum.GetValues<ImprintCategory>();
        var outcomes = new[] { ImpactOutcome.Improved, ImpactOutcome.Maintained, ImpactOutcome.Improved, ImpactOutcome.Unknown };
        var imprints = new List<Imprint>();
        for (var i = 0; i < clients.Count; i++)
        {
            var client = clients[_rng.Next(clients.Count)];
            imprints.Add(new Imprint
            {
                TenantId = tenant.Id,
                ClientId = client.Id,
                Title = ImprintTitles[_rng.Next(ImprintTitles.Length)],
                DateOccurred = Utc(DateTime.UtcNow.AddDays(-_rng.Next(1, 120))),
                Category = imprintCategories[_rng.Next(imprintCategories.Length)],
                Outcome = outcomes[_rng.Next(outcomes.Length)],
                Notes = "Recorded by case worker during follow-up.",
            });
        }
        _db.Imprints.AddRange(imprints);

        // Growth plans
        var seasons = Enum.GetValues<Season>();
        var planStatuses = new[] { GrowthPlanStatus.Active, GrowthPlanStatus.Active, GrowthPlanStatus.OnHold, GrowthPlanStatus.Completed };
        var plans = new List<GrowthPlan>();
        for (var i = 0; i < Math.Min(clients.Count, 4); i++)
        {
            var client = clients[i];
            var total = _rng.Next(3, 8);
            plans.Add(new GrowthPlan
            {
                TenantId = tenant.Id,
                ClientId = client.Id,
                AssignedToUserId = staffIds[_rng.Next(staffIds.Count)],
                Title = GrowthPlanTitles[_rng.Next(GrowthPlanTitles.Length)],
                Season = seasons[_rng.Next(seasons.Length)],
                Status = planStatuses[_rng.Next(planStatuses.Length)],
                StartDate = Utc(DateTime.UtcNow.AddDays(-_rng.Next(30, 200))),
                TargetEndDate = Utc(DateTime.UtcNow.AddDays(_rng.Next(30, 180))),
                TotalGoals = total,
                CompletedGoals = _rng.Next(0, total + 1),
            });
        }
        _db.GrowthPlans.AddRange(plans);

        // Tasks
        var taskStatuses = new[] { GrowIT.Shared.Enums.TaskStatus.Pending, GrowIT.Shared.Enums.TaskStatus.Completed };
        var tasks = new List<AppTask>();
        for (var i = 0; i < 5; i++)
        {
            var client = clients[_rng.Next(clients.Count)];
            tasks.Add(new AppTask
            {
                TenantId = tenant.Id,
                ClientId = client.Id,
                AssignedTo = staffIds[_rng.Next(staffIds.Count)],
                DueDate = Utc(DateTime.UtcNow.AddDays(_rng.Next(-10, 20))),
                Status = taskStatuses[_rng.Next(taskStatuses.Length)],
                Notes = "Follow up on documentation and next steps.",
            });
        }
        _db.Tasks.AddRange(tasks);

        // Beta feedback (so the SuperAdmin sees cross-tenant feedback)
        _db.BetaFeedbacks.Add(new BetaFeedback
        {
            TenantId = tenant.Id,
            UserId = staffIds[_rng.Next(staffIds.Count)],
            Category = "Feature Request",
            Severity = "Medium",
            Title = $"Bulk import for {tenant.Name}",
            Message = "Would love a CSV import for clients during onboarding.",
            PageUrl = "/clients",
            Status = "Open",
            CreatedAt = Utc(DateTime.UtcNow.AddDays(-_rng.Next(1, 30))),
        });

        await _db.SaveChangesAsync();
        _log.LogInformation(
            "Org '{Name}': {Clients} clients, {Inv} investments, {Imp} imprints, {Plans} growth plans.",
            tenant.Name, clients.Count, investments.Count, imprints.Count, plans.Count);
    }

    private async Task<User> EnsureSuperAdminAsync()
    {
        var existing = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == SuperAdminEmail.ToUpperInvariant());
        if (existing is not null)
        {
            await SetExclusiveRoleAsync(existing, "SuperAdmin");
            return existing;
        }

        // No account yet: create one in a fresh org so the demo is self-contained.
        var tenant = NewTenant("Hope Harbor Community Services", "Community Services", "11-50");
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        var user = await EnsureUserAsync(SuperAdminEmail, "Demonte", "Gant", "SuperAdmin", tenant.Id);
        return user;
    }

    private async Task<User> EnsureUserAsync(string email, string first, string last, string role, Guid tenantId)
    {
        var existing = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
        if (existing is not null)
        {
            await SetExclusiveRoleAsync(existing, role);
            return existing;
        }

        var user = new User
        {
            TenantId = tenantId,
            FirstName = first,
            LastName = last,
            Email = email,
            UserName = email,
            IsActive = true,
            EmailConfirmed = true,
            NotifyInviteActivity = true,
            NotifySystemAlerts = true,
        };

        var create = await _users.CreateAsync(user, DemoPassword);
        if (!create.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create demo user '{email}': {string.Join(" ", create.Errors.Select(e => e.Description))}");
        }

        await SetExclusiveRoleAsync(user, role);
        return user;
    }

    private async Task SetExclusiveRoleAsync(User user, string role)
    {
        await EnsureRoleAsync(role);
        var current = await _users.GetRolesAsync(user);
        var stale = current.Where(r => !string.Equals(r, role, StringComparison.OrdinalIgnoreCase)).ToList();
        if (stale.Count > 0) await _users.RemoveFromRolesAsync(user, stale);
        if (!await _users.IsInRoleAsync(user, role)) await _users.AddToRoleAsync(user, role);
    }

    private async Task EnsureRoleAsync(string role)
    {
        if (!await _roles.RoleExistsAsync(role))
        {
            await _roles.CreateAsync(new IdentityRole<Guid>(role));
        }
    }

    private static Tenant NewTenant(string name, string type, string size) => new()
    {
        Name = name,
        Address = "123 Mission St, Suite 200",
        ContactEmail = $"contact@{name.Split(' ')[0].ToLowerInvariant()}.org",
        OrganizationType = type,
        OrganizationSize = size,
        TrackPeople = true,
        TrackInvestments = true,
        TrackOutcomes = true,
        TrackPrograms = true,
    };

    private Client NewClient(Guid tenantId, string first, string last, HouseholdRole role) => new()
    {
        TenantId = tenantId,
        FirstName = first,
        LastName = last,
        HouseholdRole = role,
        Email = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@example.com",
        Phone = $"(555) {_rng.Next(200, 999)}-{_rng.Next(1000, 9999)}",
        Address = $"{_rng.Next(100, 9999)} Maple Ave",
        DateOfBirth = Utc(DateTime.UtcNow.AddYears(-_rng.Next(22, 65))),
        MaritalStatus = (MaritalStatus)_rng.Next(Enum.GetValues<MaritalStatus>().Length),
        EmploymentStatus = (EmploymentStatus)_rng.Next(Enum.GetValues<EmploymentStatus>().Length),
        HouseholdCount = 1,
        StabilityScore = _rng.Next(1, 11),
        LifePhase = (LifePhase)_rng.Next(Enum.GetValues<LifePhase>().Length),
        NextFollowupDate = Utc(DateTime.UtcNow.AddDays(_rng.Next(3, 45))),
    };

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private string RandomFirst() => FirstNames[_rng.Next(FirstNames.Length)];
    private string RandomLast() => LastNames[_rng.Next(LastNames.Length)];
}
