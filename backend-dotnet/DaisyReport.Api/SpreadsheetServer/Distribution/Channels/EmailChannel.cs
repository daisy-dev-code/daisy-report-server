using System.Net;
using System.Net.Mail;
using Dapper;
using DaisyReport.Api.Infrastructure;
using Serilog;

namespace DaisyReport.Api.SpreadsheetServer.Distribution.Channels;

public class EmailChannel : IDistributionChannel
{
    private readonly IDatabase _database;
    private readonly ILogger<EmailChannel> _logger;

    public EmailChannel(IDatabase database, ILogger<EmailChannel> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task<bool> DeliverAsync(string fileName, byte[] content, string contentType,
        ChannelConfig config, CancellationToken ct = default)
    {
        var smtp = await LoadSmtpSettingsAsync(config);
        if (smtp == null)
        {
            _logger.LogError("SMTP settings not found. Provide settings in channel config or configure RS_EMAIL_DATASINK.");
            return false;
        }

        if (config.Recipients.Count == 0)
        {
            _logger.LogError("No recipients specified for email delivery.");
            return false;
        }

        try
        {
            using var client = new SmtpClient(smtp.Host, smtp.Port)
            {
                EnableSsl = smtp.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrWhiteSpace(smtp.Username))
            {
                client.Credentials = new NetworkCredential(smtp.Username, smtp.Password);
            }

            var fromAddress = config.Settings.GetValueOrDefault("from", smtp.Username ?? "noreply@daisyreport.local");
            var subject = config.Settings.GetValueOrDefault("subject", $"DaisyReport: {fileName}");
            var body = config.Settings.GetValueOrDefault("body", "Please find the attached report.");

            using var message = new MailMessage
            {
                From = new MailAddress(fromAddress),
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };

            foreach (var recipient in config.Recipients)
                message.To.Add(recipient.Trim());

            using var ms = new MemoryStream(content);
            message.Attachments.Add(new Attachment(ms, fileName, contentType));

            await client.SendMailAsync(message, ct);
            _logger.LogInformation("Email sent to {Count} recipient(s): {FileName}", config.Recipients.Count, fileName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email for {FileName}", fileName);
            return false;
        }
    }

    private async Task<SmtpSettings?> LoadSmtpSettingsAsync(ChannelConfig config)
    {
        // First try channel-level settings
        if (config.Settings.TryGetValue("smtp_host", out var host) && !string.IsNullOrWhiteSpace(host))
        {
            return new SmtpSettings
            {
                Host = host,
                Port = int.TryParse(config.Settings.GetValueOrDefault("smtp_port", "587"), out var p) ? p : 587,
                UseSsl = config.Settings.GetValueOrDefault("smtp_ssl", "true").Equals("true", StringComparison.OrdinalIgnoreCase),
                Username = config.Settings.GetValueOrDefault("smtp_username"),
                Password = config.Settings.GetValueOrDefault("smtp_password")
            };
        }

        // Fall back to RS_EMAIL_DATASINK table
        try
        {
            using var conn = await _database.GetConnectionAsync();
            var row = await conn.QuerySingleOrDefaultAsync<dynamic>(
                @"SELECT host, port, encryption, username, password_encrypted
                  FROM RS_EMAIL_DATASINK
                  WHERE active = 1
                  ORDER BY id
                  LIMIT 1");

            if (row == null) return null;

            return new SmtpSettings
            {
                Host = (string)row.host,
                Port = (int)(row.port ?? 587),
                UseSsl = ((string?)row.encryption ?? "TLS").ToUpperInvariant() != "NONE",
                Username = (string?)row.username,
                Password = (string?)row.password_encrypted
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load SMTP settings from RS_EMAIL_DATASINK");
            return null;
        }
    }

    private class SmtpSettings
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public bool UseSsl { get; set; } = true;
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}
