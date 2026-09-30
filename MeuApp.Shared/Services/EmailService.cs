using System;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MeuApp.Shared.Services;

public class EmailService : IEmailService
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private readonly ILogger<EmailService> _logger;
    private readonly IConfiguration? _configuration;

    public EmailService(ILogger<EmailService> logger, IConfiguration? configuration = null)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<bool> EnviarEmailAtivacaoAsync(string nome, string email, string linkAtivacao, string codigo)
    {
        var nomeSanitizado = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(nome) ? "Usuário" : nome.Trim());
        var linkSanitizado = WebUtility.HtmlEncode(linkAtivacao?.Trim() ?? string.Empty);
        var codigoSanitizado = WebUtility.HtmlEncode(codigo?.Trim() ?? string.Empty);
        var assunto = "ValiData - Ative sua conta e defina sua senha";

        var corpoHtml = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 28px; border: 1px solid #e2e8f0; border-radius: 16px; background-color: #ffffff;'>
                <div style='text-align: center; margin-bottom: 24px;'>
                    <h2 style='color: #ea580c; margin: 0; font-size: 26px; font-weight: 800;'>ValiData</h2>
                    <p style='color: #64748b; font-size: 14px; margin-top: 4px;'>Controle Inteligente de Validades por Loja</p>
                </div>
                
                <h3 style='color: #0f172a; font-size: 18px; margin-bottom: 12px;'>Olá, {nomeSanitizado}! 👋</h3>
                <p style='color: #334155; line-height: 1.6; font-size: 15px;'>
                    Seu cadastro foi realizado no <strong>ValiData</strong>! Para validar o seu e-mail, ativar sua conta e cadastrar a sua senha pessoal, clique no botão abaixo:
                </p>

                <div style='text-align: center; margin: 32px 0;'>
                    <a href='{linkSanitizado}' style='background: linear-gradient(135deg, #ff7a18 0%, #ea580c 100%); color: #ffffff; text-decoration: none; padding: 14px 32px; border-radius: 10px; font-weight: bold; font-size: 16px; display: inline-block; box-shadow: 0 4px 12px rgba(234, 88, 12, 0.3);'>
                        🔑 Ativar Conta & Criar Senha
                    </a>
                </div>

                <div style='background: #fff7ed; border: 1px solid #fed7aa; border-radius: 10px; padding: 14px; margin: 24px 0;'>
                    <p style='margin: 0; color: #9a3412; font-size: 13px; line-height: 1.5;'>
                        <strong>Regras para a nova senha:</strong><br/>
                        • Mínimo de 6 dígitos<br/>
                        • Pelo menos 1 letra maiúscula (A-Z)<br/>
                        • Pelo menos 1 letra minúscula (a-z)<br/>
                        • Pelo menos 1 caractere especial (ex: @, #, $, %, !)
                    </p>
                </div>

                <p style='color: #94a3b8; font-size: 12px; text-align: center; margin-top: 28px; border-top: 1px solid #f1f5f9; padding-top: 16px;'>
                    Este link de ativação é seguro e expira em 48 horas. Se você não solicitou este cadastro, desconsidere este e-mail.
                </p>
            </div>
        ";

        return await DespacharEmailAsync(email, assunto, corpoHtml);
    }

    public async Task<bool> EnviarEmailConfirmacaoAsync(string nome, string email, string linkConfirmacao, string codigoConfirmacao)
    {
        var nomeSanitizado = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(nome) ? "Usuário" : nome.Trim());
        var linkSanitizado = WebUtility.HtmlEncode(linkConfirmacao?.Trim() ?? string.Empty);
        var codigoSanitizado = WebUtility.HtmlEncode(codigoConfirmacao?.Trim() ?? string.Empty);
        var assunto = "ValiData - Confirme seu Cadastro";

        var corpoHtml = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 16px; background-color: #ffffff;'>
                <div style='text-align: center; margin-bottom: 24px;'>
                    <h2 style='color: #ea580c; margin: 0; font-size: 26px; font-weight: 800;'>ValiData</h2>
                    <p style='color: #64748b; font-size: 14px; margin-top: 4px;'>Controle Inteligente de Validades por Loja</p>
                </div>
                
                <h3 style='color: #0f172a; font-size: 18px; margin-bottom: 12px;'>Olá, {nomeSanitizado}! 👋</h3>
                <p style='color: #334155; line-height: 1.6; font-size: 15px;'>
                    Obrigado por se cadastrar no ValiData. Para ativar sua conta e acessar o sistema, utilize o código de segurança abaixo ou clique no link:
                </p>

                <div style='text-align: center; margin: 28px 0;'>
                    <div style='display: inline-block; background: #fff7ed; border: 2px dashed #ea580c; border-radius: 10px; padding: 12px 28px; font-size: 2rem; font-weight: 800; letter-spacing: 6px; color: #ea580c;'>
                        {codigoSanitizado}
                    </div>
                </div>

                <div style='text-align: center; margin-bottom: 28px;'>
                    <a href='{linkSanitizado}' style='background: linear-gradient(135deg, #ff7a18 0%, #ea580c 100%); color: #ffffff; text-decoration: none; padding: 14px 32px; border-radius: 10px; font-weight: bold; font-size: 16px; display: inline-block; box-shadow: 0 4px 12px rgba(234, 88, 12, 0.3);'>
                        Validar Minha Conta Agora
                    </a>
                </div>

                <p style='color: #94a3b8; font-size: 12px; text-align: center; margin-top: 28px; border-top: 1px solid #f1f5f9; padding-top: 16px;'>
                    Este código expira em 24 horas. Se você não solicitou este cadastro, ignore esta mensagem.
                </p>
            </div>
        ";

        return await DespacharEmailAsync(email, assunto, corpoHtml);
    }

    public async Task<bool> EnviarEmailRecuperacaoSenhaAsync(string nome, string email, string linkRecuperacao, string codigo)
    {
        var nomeSanitizado = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(nome) ? "Usuário" : nome.Trim());
        var linkSanitizado = WebUtility.HtmlEncode(linkRecuperacao?.Trim() ?? string.Empty);
        var codigoSanitizado = WebUtility.HtmlEncode(codigo?.Trim() ?? string.Empty);
        var assunto = "ValiData - Redefinição de Senha";

        var corpoHtml = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 16px; background-color: #ffffff;'>
                <div style='text-align: center; margin-bottom: 24px;'>
                    <h2 style='color: #ea580c; margin: 0; font-size: 26px; font-weight: 800;'>ValiData</h2>
                    <p style='color: #64748b; font-size: 14px; margin-top: 4px;'>Redefinição Segura de Senha</p>
                </div>
                
                <h3 style='color: #0f172a; font-size: 18px; margin-bottom: 12px;'>Olá, {nomeSanitizado}!</h3>
                <p style='color: #334155; line-height: 1.6; font-size: 15px;'>
                    Recebemos uma solicitação para redefinir a senha da sua conta no ValiData. Use o código de 6 dígitos abaixo ou clique no link para cadastrar uma nova senha:
                </p>

                <div style='text-align: center; margin: 28px 0;'>
                    <div style='display: inline-block; background: #fff7ed; border: 2px dashed #ea580c; border-radius: 10px; padding: 12px 28px; font-size: 2rem; font-weight: 800; letter-spacing: 6px; color: #ea580c;'>
                        {codigoSanitizado}
                    </div>
                </div>

                <div style='text-align: center; margin-bottom: 28px;'>
                    <a href='{linkSanitizado}' style='background: linear-gradient(135deg, #ff7a18 0%, #ea580c 100%); color: #ffffff; text-decoration: none; padding: 14px 32px; border-radius: 10px; font-weight: bold; font-size: 16px; display: inline-block; box-shadow: 0 4px 12px rgba(234, 88, 12, 0.3);'>
                        Redefinir Minha Senha
                    </a>
                </div>

                <p style='color: #94a3b8; font-size: 12px; text-align: center; margin-top: 28px; border-top: 1px solid #f1f5f9; padding-top: 16px;'>
                    Este link expira em 2 horas. Se você não solicitou a redefinição de senha, ignore este e-mail.
                </p>
            </div>
        ";

        return await DespacharEmailAsync(email, assunto, corpoHtml);
    }

    private async Task<bool> DespacharEmailAsync(string destinatarioEmail, string assunto, string corpoHtml)
    {
        var resendApiKey = _configuration?["EmailSettings:ResendApiKey"]
                           ?? _configuration?["EmailSettings:SmtpPass"]
                           ?? Environment.GetEnvironmentVariable("RESEND_API_KEY");

        // Se houver chave do Resend configurada (começa com "re_"), prioriza a API REST oficial (HTTPS)
        if (!string.IsNullOrWhiteSpace(resendApiKey) && resendApiKey.StartsWith("re_"))
        {
            var enviadoViaResend = await EnviarViaResendApiAsync(resendApiKey, destinatarioEmail, assunto, corpoHtml);
            if (enviadoViaResend) return true;
        }

        // Fallback para envio SMTP tradicional
        return await EnviarEmailViaSmtpAsync(destinatarioEmail, assunto, corpoHtml);
    }

    private async Task<bool> EnviarViaResendApiAsync(string apiKey, string destinatarioEmail, string assunto, string corpoHtml)
    {
        var senderName = _configuration?["EmailSettings:SenderName"] ?? "ValiData";
        var senderEmail = _configuration?["EmailSettings:SenderEmail"];

        if (string.IsNullOrWhiteSpace(senderEmail))
        {
            senderEmail = "onboarding@resend.dev";
        }

        var emailMascarado = MascararEmail(destinatarioEmail);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());

            var payload = new
            {
                from = $"{senderName} <{senderEmail}>",
                to = new[] { destinatarioEmail },
                subject = assunto,
                html = corpoHtml
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation($"[RESEND ENVIADO] Para: {emailMascarado} | Assunto: {assunto}");
                return true;
            }
            else
            {
                _logger.LogWarning($"[RESEND FALHA] Status: {response.StatusCode} | Para: {emailMascarado}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[RESEND EXCEÇÃO] Erro ao enviar e-mail para {emailMascarado}: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> EnviarEmailViaSmtpAsync(string destinatarioEmail, string assunto, string corpoHtml)
    {
        var smtpHost = _configuration?["EmailSettings:SmtpHost"]
                       ?? Environment.GetEnvironmentVariable("SMTP_HOST");
        var smtpPortStr = _configuration?["EmailSettings:SmtpPort"]
                          ?? Environment.GetEnvironmentVariable("SMTP_PORT");
        var smtpUser = _configuration?["EmailSettings:SmtpUser"]
                       ?? Environment.GetEnvironmentVariable("SMTP_USER");
        var smtpPass = _configuration?["EmailSettings:SmtpPass"]
                       ?? Environment.GetEnvironmentVariable("SMTP_PASS");
        var senderName = _configuration?["EmailSettings:SenderName"]
                         ?? "ValiData";
        var senderEmail = _configuration?["EmailSettings:SenderEmail"]
                          ?? smtpUser;

        var emailMascarado = MascararEmail(destinatarioEmail);

        if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass))
        {
            _logger.LogWarning($"[SMTP NÃO CONFIGURADO] Configure 'EmailSettings' no appsettings.json ou variáveis de ambiente para envio de e-mails para {emailMascarado}.");
            return false;
        }

#pragma warning disable CA1416
        try
        {
            int smtpPort = int.TryParse(smtpPortStr, out var p) ? p : 587;
            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(smtpUser, smtpPass),
                EnableSsl = true
            };

            var fromAddress = !string.IsNullOrWhiteSpace(senderEmail) ? senderEmail : smtpUser;
            var mail = new MailMessage
            {
                From = new MailAddress(fromAddress, senderName),
                Subject = assunto,
                Body = corpoHtml,
                IsBodyHtml = true
            };
            mail.To.Add(destinatarioEmail);

            await client.SendMailAsync(mail);
            _logger.LogInformation($"[SMTP ENVIADO] Para: {emailMascarado} | Assunto: {assunto}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[SMTP FALHA] Erro ao enviar e-mail para {emailMascarado}: {ex.Message}");
            return false;
        }
#pragma warning restore CA1416
    }

    private static string MascararEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return "***";
        var partes = email.Split('@');
        var usuario = partes[0];
        var prefixo = usuario.Length > 2 ? usuario[..2] : usuario[..1];
        return $"{prefixo}***@{partes[1]}";
    }
}
