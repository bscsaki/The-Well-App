using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TheWell.Core.DTOs;

namespace TheWell.MAUI.Services;

public class ApiService
{
    private readonly HttpClient _http;
    private const string AccessTokenKey = "access_token";
    private const string RefreshTokenKey = "refresh_token";

    // App.xaml.cs subscribes to this to navigate back to login
    public static event Action? SessionExpired;

    public ApiService(HttpClient http) => _http = http;

    private async Task AttachTokenAsync()
    {
        var token = await SecureStorage.GetAsync(AccessTokenKey);
        if (token is not null)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    // Wraps any HTTP call: returns null on timeout or network failure instead of throwing.
    private static async Task<HttpResponseMessage?> SafeCallAsync(Func<Task<HttpResponseMessage>> call)
    {
        try { return await call(); }
        catch (TaskCanceledException) { return null; }
        catch (HttpRequestException) { return null; }
    }

    /// <summary>
    /// Sends an authenticated request. On 401 attempts one token refresh then retries.
    /// Fires SessionExpired if still unauthorized after the retry.
    /// Returns null on network failure.
    /// </summary>
    private async Task<HttpResponseMessage?> SendWithRefreshAsync(Func<Task<HttpResponseMessage>> makeRequest)
    {
        await AttachTokenAsync();
        var response = await SafeCallAsync(makeRequest);
        if (response is null) return null;

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var refreshed = await TryRefreshAsync();
            if (refreshed)
            {
                await AttachTokenAsync();
                response = await SafeCallAsync(makeRequest);
                if (response is null) return null;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                ClearTokens();
                SessionExpired?.Invoke();
            }
        }

        return response;
    }

    private async Task<bool> TryRefreshAsync()
    {
        var refreshToken = await SecureStorage.GetAsync(RefreshTokenKey);
        if (string.IsNullOrEmpty(refreshToken)) return false;

        try
        {
            // Send without the expired Authorization header
            _http.DefaultRequestHeaders.Authorization = null;
            var response = await _http.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(refreshToken));
            if (!response.IsSuccessStatusCode) return false;

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var newAccess = body.TryGetProperty("accessToken", out var a) ? a.GetString() : null;
            var newRefresh = body.TryGetProperty("refreshToken", out var r) ? r.GetString() : null;

            if (newAccess is null || newRefresh is null) return false;

            await SecureStorage.SetAsync(AccessTokenKey, newAccess);
            await SecureStorage.SetAsync(RefreshTokenKey, newRefresh);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var response = await SafeCallAsync(() => _http.PostAsJsonAsync("api/auth/login", request));
        if (response is null || !response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (result is not null)
        {
            await SecureStorage.SetAsync(AccessTokenKey, result.AccessToken);
            await SecureStorage.SetAsync(RefreshTokenKey, result.RefreshToken);
        }
        return result;
    }

    public async Task<bool> ForceResetAsync(string newPassword)
    {
        var response = await SendWithRefreshAsync(
            () => _http.PostAsJsonAsync("api/auth/force-reset", new ForceResetRequest(newPassword)));
        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<bool> PasswordResetAsync(string token, string newPassword)
    {
        var response = await SafeCallAsync(() => _http.PostAsJsonAsync("api/auth/password-reset",
            new PasswordResetRequest(token, newPassword)));
        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<bool> RequestOtpAsync(string email)
    {
        var response = await SafeCallAsync(() => _http.PostAsJsonAsync("api/auth/otp/request", new OtpRequestDto(email)));
        return response?.IsSuccessStatusCode ?? false;
    }

    public async Task<OtpVerifyResponse?> VerifyOtpAsync(string email, string otp)
    {
        var response = await SafeCallAsync(() => _http.PostAsJsonAsync("api/auth/otp/verify", new OtpVerifyRequest(email, otp)));
        if (response is null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<OtpVerifyResponse>();
    }

    // ── Intake ────────────────────────────────────────────────────────────────

    public async Task<IntakeResponse?> GetIntakeAsync()
    {
        var response = await SendWithRefreshAsync(() => _http.GetAsync("api/intake"));
        if (response is null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<IntakeResponse>();
    }

    public async Task<IntakeResponse?> SubmitIntakeAsync(SubmitIntakeRequest request)
    {
        var response = await SendWithRefreshAsync(() => _http.PostAsJsonAsync("api/intake", request));
        if (response is null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<IntakeResponse>();
    }

    // ── Goals ─────────────────────────────────────────────────────────────────

    public async Task<GoalResponse?> GetGoalAsync()
    {
        var response = await SendWithRefreshAsync(() => _http.GetAsync("api/goals"));
        if (response is null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<GoalResponse>();
    }

    public async Task<GoalResponse?> CreateGoalAsync(CreateGoalRequest request)
    {
        var response = await SendWithRefreshAsync(() => _http.PostAsJsonAsync("api/goals", request));
        if (response is null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<GoalResponse>();
    }

    // ── Daily Logs ────────────────────────────────────────────────────────────

    public async Task<List<DailyLogResponse>> GetLogsAsync()
    {
        var response = await SendWithRefreshAsync(() => _http.GetAsync("api/logs"));
        if (response is null || !response.IsSuccessStatusCode) return [];
        return await response.Content.ReadFromJsonAsync<List<DailyLogResponse>>() ?? [];
    }

    public async Task<DailyLogResponse?> CreateLogAsync(CreateLogRequest request)
    {
        var response = await SendWithRefreshAsync(() => _http.PostAsJsonAsync("api/logs", request));
        if (response is null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<DailyLogResponse>();
    }

    public async Task<DailyLogResponse?> UpdateLogAsync(Guid logId, UpdateLogRequest request)
    {
        var response = await SendWithRefreshAsync(() => _http.PutAsJsonAsync($"api/logs/{logId}", request));
        if (response is null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<DailyLogResponse>();
    }

    // ── Stats ─────────────────────────────────────────────────────────────────

    public async Task<StatsResponse?> GetStatsAsync()
    {
        var response = await SendWithRefreshAsync(() => _http.GetAsync("api/stats"));
        if (response is null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<StatsResponse>();
    }

    // ── Content ───────────────────────────────────────────────────────────────

    public async Task<WeeklyContentResponse?> GetCurrentWeekContentAsync()
    {
        var response = await SendWithRefreshAsync(() => _http.GetAsync("api/content/current-week"));
        if (response is null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<WeeklyContentResponse>();
    }

    public async Task<List<WeekSummaryResponse>> GetAllWeeksAsync()
    {
        var response = await SendWithRefreshAsync(() => _http.GetAsync("api/content/weeks"));
        if (response is null || !response.IsSuccessStatusCode) return [];
        return await response.Content.ReadFromJsonAsync<List<WeekSummaryResponse>>() ?? [];
    }

    public async Task<WeeklyContentResponse?> GetWeekContentAsync(int weekNumber)
    {
        var response = await SendWithRefreshAsync(() => _http.GetAsync($"api/content/weeks/{weekNumber}"));
        if (response is null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<WeeklyContentResponse>();
    }

    // ── Users ─────────────────────────────────────────────────────────────────

    public async Task<UserProfileResponse?> GetProfileAsync()
    {
        var response = await SendWithRefreshAsync(() => _http.GetAsync("api/users/me"));
        if (response is null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UserProfileResponse>();
    }

    public async Task<(bool success, string message)> ChangePasswordAsync(ChangePasswordRequest request)
    {
        var response = await SendWithRefreshAsync(() => _http.PutAsJsonAsync("api/users/me/password", request));
        if (response is null) return (false, "Could not connect to server.");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var msg = body.TryGetProperty("message", out var m) ? m.GetString() ?? ""
                : body.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
        return (response.IsSuccessStatusCode, msg);
    }

    // ── Config ────────────────────────────────────────────────────────────────

    public async Task<DateOnly?> GetCourseStartDateAsync()
    {
        var response = await SafeCallAsync(() => _http.GetAsync("api/config"));
        if (response is null || !response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (body.TryGetProperty("startDate", out var sd) && sd.ValueKind != JsonValueKind.Null)
        {
            if (DateOnly.TryParse(sd.GetString(), out var date)) return date;
        }
        return null;
    }

    public async Task<string> PopulateWeeksAsync()
    {
        var response = await SafeCallAsync(() => _http.PostAsync("api/content/populate", null));
        if (response is null) return "Could not connect to server.";
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("message", out var m) ? m.GetString() ?? ""
             : body.TryGetProperty("error",   out var e) ? e.GetString() ?? "" : "";
    }

    // ── Session ───────────────────────────────────────────────────────────────

    public void ClearTokens()
    {
        SecureStorage.Remove(AccessTokenKey);
        SecureStorage.Remove(RefreshTokenKey);
    }
}
