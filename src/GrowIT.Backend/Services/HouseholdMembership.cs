using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.Backend.Services;

/// <summary>
/// One place for "who is in which household, and who is its head", so the household card
/// (Household.PrimaryClientId) and each person's role (Client.HouseholdRole) cannot disagree,
/// whichever screen changed them. Adds/changes tracked entities only; the caller saves.
/// </summary>
public static class HouseholdMembership
{
    /// <summary>A person's role when they are not grouped into any household.</summary>
    public const HouseholdRole UnlinkedRole = HouseholdRole.Head;

    /// <summary>Links <paramref name="client"/> to <paramref name="household"/> with <paramref name="role"/>.</summary>
    public static async Task AssignAsync(ApplicationDbContext db, Client client, Household household, HouseholdRole role)
    {
        // Moving out of another household: that household must not keep them as its head.
        if (client.HouseholdId is { } previousHouseholdId && previousHouseholdId != household.Id)
        {
            await ClearHeadIfAsync(db, previousHouseholdId, client.Id);
        }

        client.HouseholdId = household.Id;
        client.HouseholdRole = role;

        if (role == HouseholdRole.Head)
        {
            if (household.PrimaryClientId is { } previousHeadId && previousHeadId != client.Id)
            {
                var previousHead = await db.Clients
                    .FirstOrDefaultAsync(c => c.Id == previousHeadId && c.HouseholdId == household.Id);
                if (previousHead is { HouseholdRole: HouseholdRole.Head })
                {
                    previousHead.HouseholdRole = HouseholdRole.Other;
                }
            }

            household.PrimaryClientId = client.Id;
        }
        else if (household.PrimaryClientId == client.Id)
        {
            household.PrimaryClientId = null;
        }
    }

    /// <summary>Takes <paramref name="client"/> out of their household (they stay in People).</summary>
    public static async Task UnlinkAsync(ApplicationDbContext db, Client client)
    {
        if (client.HouseholdId is { } householdId)
        {
            await ClearHeadIfAsync(db, householdId, client.Id);
        }

        client.HouseholdId = null;
        client.HouseholdRole = UnlinkedRole;
    }

    private static async Task ClearHeadIfAsync(ApplicationDbContext db, Guid householdId, Guid clientId)
    {
        var household = await db.Households.FirstOrDefaultAsync(h => h.Id == householdId);
        if (household is not null && household.PrimaryClientId == clientId)
        {
            household.PrimaryClientId = null;
        }
    }
}
