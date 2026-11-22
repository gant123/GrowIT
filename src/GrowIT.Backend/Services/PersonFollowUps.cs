using System.Security.Cryptography;
using System.Text;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using TaskStatus = GrowIT.Shared.Enums.TaskStatus;

namespace GrowIT.Backend.Services;

/// <summary>
/// Keeps a person's "Next follow-up date" and the Follow-ups list in step. Setting the date on a
/// profile used to do nothing visible: the date was stored on the person and never became a
/// follow-up anyone could see, complete or assign.
///
/// The follow-up created from a profile date gets a deterministic id derived from
/// (tenant, person, due date), the same idea as the IdempotencyKey/fingerprint used for report
/// runs and beta feedback, but stored in the primary key because the Tasks table has no
/// dedup column (and this change must not add a migration). Because the primary key is unique,
/// re-sending the same save can never produce a second follow-up, and the one created for a
/// date can always be found again when the date changes or is cleared.
/// </summary>
public static class PersonFollowUps
{
    private const string KeyScope = "person-next-follow-up";

    /// <summary>The id of the follow-up created from a person's next follow-up date.</summary>
    public static Guid KeyFor(Guid tenantId, Guid clientId, DateTime dueDate)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{KeyScope}|{tenantId:N}|{clientId:N}|{dueDate.Date:yyyy-MM-dd}"));
        var bytes = hash.AsSpan(0, 16).ToArray();

        // Mark it as an RFC 9562 version 8 (custom) UUID so it is still a well-formed id.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>True when <paramref name="task"/> is the follow-up for the person's current next follow-up date.</summary>
    public static bool IsFromNextFollowUpDate(Guid taskId, Guid tenantId, Guid clientId, DateTime? nextFollowupDate) =>
        nextFollowupDate.HasValue && taskId == KeyFor(tenantId, clientId, nextFollowupDate.Value);

    public static string DefaultNotes(string firstName, string lastName)
    {
        var name = $"{firstName} {lastName}".Trim();
        return string.IsNullOrEmpty(name) ? "Check in" : $"Check in with {name}";
    }

    /// <summary>
    /// Applies a change of <paramref name="client"/>'s next follow-up date (already set on the
    /// entity) to the Follow-ups list. Adds/removes tracked entities only; the caller saves.
    /// <list type="bullet">
    /// <item>Date set (or moved): a pending "Check in with ..." follow-up exists for the new date.
    /// A pending one for the old date is replaced (keeping its assignee and any edited text);
    /// a completed or skipped one is left alone as history.</item>
    /// <item>Date cleared: only the pending follow-up that came from the old date is removed.
    /// Follow-ups people added by hand are never touched.</item>
    /// <item>Date unchanged: an existing follow-up (in any status) is left as it is, so a completed
    /// one is not reopened on every profile save; only the default title follows a rename. If
    /// there is none yet (date saved before this sync existed) it is created.</item>
    /// </list>
    /// </summary>
    public static async Task SyncAsync(
        ApplicationDbContext db,
        Client client,
        DateTime? previousDate,
        string previousDefaultNotes,
        Guid? actingUserId,
        CancellationToken ct = default)
    {
        var newDate = client.NextFollowupDate?.Date;
        var oldDate = previousDate?.Date;
        var newDefaultNotes = DefaultNotes(client.FirstName, client.LastName);

        AppTask? previous = null;
        if (oldDate.HasValue)
        {
            var previousKey = KeyFor(client.TenantId, client.Id, oldDate.Value);
            previous = await db.Tasks.FirstOrDefaultAsync(t => t.Id == previousKey && t.ClientId == client.Id, ct);
        }

        var previousIsPending = previous is { Status: TaskStatus.Pending };

        if (oldDate == newDate)
        {
            if (previousIsPending && previous!.Notes == previousDefaultNotes && previousDefaultNotes != newDefaultNotes)
            {
                previous.Notes = newDefaultNotes;
                previous.UpdatedAt = DateTime.UtcNow;
            }

            // Unchanged date that already has its follow-up (pending, completed or skipped): done.
            // With no follow-up at all (a date saved before this sync existed), fall through and
            // create it, so re-saving such a profile brings Follow-ups up to date. Deleting the
            // follow-up in Follow-ups clears the profile date, so this never revives a deleted one.
            if (previous is not null || !newDate.HasValue)
            {
                return;
            }
        }

        if (previousIsPending)
        {
            db.Tasks.Remove(previous!);
        }

        if (!newDate.HasValue)
        {
            return;
        }

        var key = KeyFor(client.TenantId, client.Id, newDate.Value);
        if (await db.Tasks.AnyAsync(t => t.Id == key, ct))
        {
            // Already there (e.g. the date was set back to a day whose follow-up exists).
            return;
        }

        var assignee = await ResolveAssigneeAsync(db, client.TenantId, previous?.AssignedTo, actingUserId, ct);
        if (assignee is null)
        {
            // A follow-up must have an owner; with no active team member there is nobody to give it to.
            return;
        }

        var creator = actingUserId.HasValue && await IsActiveTeamMemberAsync(db, client.TenantId, actingUserId.Value, ct)
            ? actingUserId
            : null;

        var keepEditedText = previousIsPending &&
                             !string.IsNullOrWhiteSpace(previous!.Notes) &&
                             previous.Notes != previousDefaultNotes;

        db.Tasks.Add(new AppTask
        {
            Id = key,
            TenantId = client.TenantId,
            ClientId = client.Id,
            AssignedTo = assignee.Value,
            CreatedByUserId = creator,
            Type = ActionItemType.ClientFollowUp,
            DueDate = DateTime.SpecifyKind(newDate.Value, DateTimeKind.Utc),
            Status = TaskStatus.Pending,
            Notes = keepEditedText ? previous!.Notes : newDefaultNotes,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static async Task<Guid?> ResolveAssigneeAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid? previousAssignee,
        Guid? actingUserId,
        CancellationToken ct)
    {
        if (previousAssignee.HasValue && await IsActiveTeamMemberAsync(db, tenantId, previousAssignee.Value, ct))
        {
            return previousAssignee;
        }

        if (actingUserId.HasValue && await IsActiveTeamMemberAsync(db, tenantId, actingUserId.Value, ct))
        {
            return actingUserId;
        }

        // e.g. a platform admin acting inside the workspace: give it to the longest-standing member.
        var fallback = await db.Users
            .Where(u => u.TenantId == tenantId && u.IsActive)
            .OrderBy(u => u.CreatedAt)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
        return fallback;
    }

    private static Task<bool> IsActiveTeamMemberAsync(ApplicationDbContext db, Guid tenantId, Guid userId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id == userId && u.TenantId == tenantId && u.IsActive, ct);
}
