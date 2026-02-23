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
    Task<UserProfileDto> RemoveProfilePhotoAsync();
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
        IBrowserFile fileToUpload = file;
        try
        {
            // Browser-side resize/compression keeps uploads small and consistent without server image libraries.
            fileToUpload = await file.RequestImageFileAsync("image/jpeg", 512, 512);
        }
        catch
        {
            // Fall back to the original file if the browser cannot transform it.
        }

        using var form = new MultipartFormDataContent();
        using var stream = fileToUpload.OpenReadStream(5 * 1024 * 1024);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(fileToUpload.ContentType);
        form.Add(content, "file", string.IsNullOrWhiteSpace(fileToUpload.Name) ? "profile.jpg" : fileToUpload.Name);

        var response = await _http.PostAsync("api/profile/photo", form);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception(string.IsNullOrWhiteSpace(body) ? "Photo upload failed." : body);
        }

        var payload = await response.Content.ReadFromJsonAsync<UserProfileDto>(_jsonOptions);
        return payload ?? throw new Exception("Photo upload completed but no profile payload was returned.");
    }

    public async Task<UserProfileDto> RemoveProfilePhotoAsync() =>
        (await DeleteAsync<UserProfileDto>("api/profile/photo"))!;

    public Task ChangePasswordAsync(ChangePasswordRequest request) =>
        PostAsync("api/profile/change-password", request);
}
