using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace BookLibrary.Services;

// Email is sent through Resend's web API (https://resend.com). Render's free plan blocks the normal
// email (SMTP) ports, so a web API is the only way to send from there.
//
// Settings (user secrets locally, environment variables with __ on Render):
//   Email:Resend:ApiKey   the Resend API key (secret)
//   Email:From            the sender, e.g. "Book Library <noreply@yourdomain.com>"
// With both set the site sends emails, new accounts must confirm their email address, and
// "Forgot your password?" emails a reset link. Without them nothing is sent and none of that applies.
public static class EmailSettings
{
    public static bool IsConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config["Email:Resend:ApiKey"]) &&
        !string.IsNullOrWhiteSpace(config["Email:From"]);

    // Requests per IP address per 15 minutes to the pages that send email (register, forgot password,
    // resend confirmation), so nobody can use the site to flood someone's inbox.
    public static int RequestsPer15Minutes(IConfiguration config) =>
        int.TryParse(config["Email:RequestsPer15Minutes"], out var n) && n > 0 ? n : 10;
}

public class ResendEmailSender(IHttpClientFactory httpFactory, IConfiguration config, ILogger<ResendEmailSender> log) : IEmailSender
{
    public const string HttpClientName = "resend";

    // Never throws: a failed email must not break registering or the forgot-password page.
    // Failures are logged; the person can use "Resend email confirmation" or ask an admin.
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        if (!EmailSettings.IsConfigured(config))
        {
            log.LogInformation("Email is not configured, so no email was sent (subject: {Subject}).", subject);
            return;
        }

        try
        {
            var body = JsonSerializer.Serialize(new
            {
                from = config["Email:From"]!.Trim(),
                to = new[] { email },
                subject,
                html = htmlMessage,
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config["Email:Resend:ApiKey"]!.Trim());

            using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request);
            if (response.IsSuccessStatusCode)
                log.LogInformation("Sent email \"{Subject}\".", subject);
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                log.LogError("Resend refused the email \"{Subject}\": {Status} {Error}", subject, (int)response.StatusCode,
                    error.Length <= 300 ? error : error[..300]);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogError(ex, "Could not reach Resend to send \"{Subject}\".", subject);
        }
    }
}
