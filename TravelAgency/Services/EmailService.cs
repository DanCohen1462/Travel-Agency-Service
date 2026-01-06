using System;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace TravelAgency.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public void SendWithAttachment(string toEmail, string subject, string body, byte[] attachmentBytes, string attachmentFileName)
        {
            var host = _config["Smtp:Host"];
            var portStr = _config["Smtp:Port"];
            var user = _config["Smtp:Username"];
            var pass = Environment.GetEnvironmentVariable("SMTP_PASSWORD")
                       ?? _config["Smtp:Password"];

            var from = _config["Smtp:From"];
            var enableSslStr = _config["Smtp:EnableSsl"];

            if (string.IsNullOrWhiteSpace(host) ||
                string.IsNullOrWhiteSpace(portStr) ||
                string.IsNullOrWhiteSpace(user) ||
                string.IsNullOrWhiteSpace(pass) ||
                string.IsNullOrWhiteSpace(from))
            {
                throw new InvalidOperationException("SMTP settings are missing in appsettings.json (Smtp section).");
            }

            int port = int.TryParse(portStr, out var p) ? p : 587;
            bool enableSsl = !string.IsNullOrWhiteSpace(enableSslStr) && bool.Parse(enableSslStr);

            using var msg = new MailMessage();
            msg.From = new MailAddress(from, "TravelAgency");
            msg.To.Add(toEmail);
            msg.Subject = subject;
            msg.Body = body;
            msg.IsBodyHtml = false;

            // attachment
            if (attachmentBytes != null && attachmentBytes.Length > 0)
            {
                var stream = new System.IO.MemoryStream(attachmentBytes);
                var attachment = new Attachment(stream, attachmentFileName, "application/pdf");
                msg.Attachments.Add(attachment);
            }

            using var client = new SmtpClient(host, port);
            client.EnableSsl = enableSsl;
            client.Credentials = new NetworkCredential(user, pass);

                      client.Send(msg);
        }

        public void SendWithAttachments(
            string toEmail,
            string subject,
            string body,
            (byte[] Bytes, string FileName, string ContentType)[] attachments)
        {
            var host = _config["Smtp:Host"];
            var portStr = _config["Smtp:Port"];
            var user = _config["Smtp:Username"];
            var pass = _config["Smtp:Password"];
            var from = _config["Smtp:From"];
            var enableSslStr = _config["Smtp:EnableSsl"];

            if (string.IsNullOrWhiteSpace(host) ||
                string.IsNullOrWhiteSpace(portStr) ||
                string.IsNullOrWhiteSpace(user) ||
                string.IsNullOrWhiteSpace(pass) ||
                string.IsNullOrWhiteSpace(from))
            {
                throw new InvalidOperationException("SMTP settings are missing in appsettings.json (Smtp section).");
            }

            int port = int.TryParse(portStr, out var p) ? p : 587;
            bool enableSsl = !string.IsNullOrWhiteSpace(enableSslStr) && bool.Parse(enableSslStr);

            using var msg = new MailMessage();
            msg.From = new MailAddress(from, "TravelAgency");
            msg.To.Add(toEmail);
            msg.Subject = subject;
            msg.Body = body;
            msg.IsBodyHtml = false;

            if (attachments != null && attachments.Length > 0)
            {
                foreach (var a in attachments)
                {
                    if (a.Bytes == null || a.Bytes.Length == 0) continue;
                    var stream = new System.IO.MemoryStream(a.Bytes);
                    var ct = string.IsNullOrWhiteSpace(a.ContentType) ? "application/octet-stream" : a.ContentType;
                    var attachment = new Attachment(stream, a.FileName, ct);
                    msg.Attachments.Add(attachment);
                }
            }

            using var client = new SmtpClient(host, port);
            client.EnableSsl = enableSsl;
            client.Credentials = new NetworkCredential(user, pass);

            client.Send(msg);
        }
    }
}
