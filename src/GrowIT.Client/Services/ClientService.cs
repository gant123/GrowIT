using System.Net;
using System.Net.Http.Json;
using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface IClientService
{
    Task<List<ClientDto>> GetAllClientsAsync();
    Task<ClientDetailDto> GetClientDetailAsync(Guid id);
    Task<Guid> CreateClientAsync(CreateClientRequest request);
    Task UpdateClientAsync(Guid id, CreateClientRequest request);
    Task<ClientDeleteOutcome> DeleteClientAsync(Guid id);
    Task AddFamilyMemberAsync(Guid clientId, CreateFamilyMemberRequest request);
    Task<FamilyMemberProfileDto> GetMemberProfileAsync(Guid memberId);
    Task UpdateFamilyMemberAsync(Guid memberId, CreateFamilyMemberRequest request);
    Task DeleteFamilyMemberAsync(Guid memberId);
}

public class ClientService : BaseApiService, IClientService
{
    private const string Endpoint = "api/clients";

    public ClientService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<List<ClientDto>> GetAllClientsAsync()
    {
        return await GetAsync<List<ClientDto>>(Endpoint) 
               ?? new List<ClientDto>();
    }

    public async Task<ClientDetailDto> GetClientDetailAsync(Guid id)
    {
        return await GetAsync<ClientDetailDto>($"{Endpoint}/{id}")
               ?? throw new Exception("Client not found");
    }

    public async Task<Guid> CreateClientAsync(CreateClientRequest request)
    {
        // POST api/clients
        var result = await PostAsync<CreateClientRequest, CreateResponse>(Endpoint, request);
        return result?.ClientId ?? Guid.Empty;
    }

    public async Task UpdateClientAsync(Guid id, CreateClientRequest request)
    {
        await PutAsync($"{Endpoint}/{id}", request);
    }

    public async Task<ClientDeleteOutcome> DeleteClientAsync(Guid id)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.DeleteAsync($"{Endpoint}/{id}");
        }
        catch (HttpRequestException ex)
        {
            const string message = "We couldn't reach the server. Check your connection and try again.";
            _notifications.Error(message);
            throw new ApiException(message, ex);
        }

        // "Still has records" is an expected answer, shown inside the delete dialog, so it is
        // returned rather than raised as an error toast.
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            ClientDeleteBlockedDto? blocked = null;
            try
            {
                blocked = await response.Content.ReadFromJsonAsync<ClientDeleteBlockedDto>(_jsonOptions);
            }
            catch (System.Text.Json.JsonException)
            {
            }

            return ClientDeleteOutcome.Blocked(
                string.IsNullOrWhiteSpace(blocked?.Message)
                    ? "This person still has records linked to their profile, so they can't be deleted."
                    : blocked!.Message);
        }

        await EnsureSuccessWithDetailsAsync(response);
        var result = await response.Content.ReadFromJsonAsync<ClientDeleteResultDto>(_jsonOptions);
        return ClientDeleteOutcome.Deleted(result?.Message ?? "Deleted.");
    }

    public async Task AddFamilyMemberAsync(Guid clientId, CreateFamilyMemberRequest request)
    {
        // POST api/clients/{id}/members
        await PostAsync($"{Endpoint}/{clientId}/members", request);
    }

    public async Task<FamilyMemberProfileDto> GetMemberProfileAsync(Guid memberId)
    {
        // GET api/clients/members/{memberId}
        return await GetAsync<FamilyMemberProfileDto>($"{Endpoint}/members/{memberId}")
               ?? throw new Exception("Member not found");
    }

    public async Task UpdateFamilyMemberAsync(Guid memberId, CreateFamilyMemberRequest request)
    {
        // PUT api/clients/members/{memberId}
        await PutAsync($"{Endpoint}/members/{memberId}", request);
    }

    public async Task DeleteFamilyMemberAsync(Guid memberId)
    {
        // DELETE api/clients/members/{memberId}
        await DeleteAsync($"{Endpoint}/members/{memberId}");
    }

    // Helper class to catch the anonymous object return { ClientId = ... }
    private class CreateResponse { public Guid ClientId { get; set; } }
}

/// <summary>What happened when deleting a person: deleted, or refused because records are linked.</summary>
public sealed record ClientDeleteOutcome(bool Succeeded, string Message)
{
    public static ClientDeleteOutcome Deleted(string message) => new(true, message);
    public static ClientDeleteOutcome Blocked(string message) => new(false, message);
}
