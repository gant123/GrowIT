using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Components.Forms;
using System.Net.Http.Json;

namespace GrowIT.Client.Services;

public interface IProfileService
{
    Task<UserProfileDto> GetProfileAsync();
    Task<UserProfileDto> UpdateProfileAsync(UpdateUserProfileRequest request);
    Task<UserProfileDto> UpdateNotificationPreferencesAsync(UpdateNotificationPreferencesRequest request);
    Task<UserProfileDto> UploadProfilePhotoAsync(IBrowserFile file);
    Task ChangePasswordAsync(ChangePasswordRequest request);
}

public class ProfileService : BaseApiService, IProfileService
{
    public ProfileService(HttpClient http) : base(http) { }

    public async Task<UserProfileDto> GetProfileAsync() =>
        (await GetAsync<UserProfileDto>("api/profile"))!;

    public async Task<UserProfileDto> UpdateProfileAsync(UpdateUserProfileRequest request) =>
        (await PutAsync<UpdateUserProfileRequest, UserProfileDto>("api/profile", request))!;

    public async Task<UserProfileDto> UpdateNotificationPreferencesAsync(UpdateNotificationPreferencesRequest request) =>
        (await PutAsync<UpdateNotificationPreferencesRequest, UserProfileDto>("api/profile/notification-preferences", request))!;

    public async Task<UserProfileDto> UploadProfilePhotoAsync(IBrowserFile file)
    {
        using var form = new MultipartFormDataContent();
        using var stream = file.OpenReadStream(5 * 1024 * 1024);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
        form.Add(content, "file", file.Name);

        var response = await _http.PostAsync("api/profile/photo", form);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception(string.IsNullOrWhiteSpace(body) ? "Photo upload failed." : body);
        }

        var payload = await response.Content.ReadFromJsonAsync<UserProfileDto>(_jsonOptions);
        return payload ?? throw new Exception("Photo upload completed but no profile payload was returned.");
    }

    public Task ChangePasswordAsync(ChangePasswordRequest request) =>
        PostAsync("api/profile/change-password", request);
}
