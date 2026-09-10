using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MeuApp.Shared.Services;

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;
    private static UltimoEmailEnviado? _ultimoEmailSimulado;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
    }

    public UltimoEmailEnviado? ObterUltimoEmailSimulado()
    {
        return _ultimoEmailSimulado;
    }

    public async Task<bool> EnviarEmailConfirmacaoAsync(string nome, string email, string linkConfirmacao, string codigoConfirmacao)
    {
        var assunto = "ValiData - Confirme seu Cadastro";
        var corpoHtml = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 12px;'>
                <div style='text-align: center; margin-bottom: 20px;'>
                    <h2 style='color: #4f46e5; margin: 0;'>ValiData</h2>
                    <p style='color: #64748b; font-size: 0.9rem;'>Controle Inteligente de Validades</p>
                </div>
                
                <h3 style='color: #0f172a;'>Olá, {nome}!</h3>
                <p style='color: #334155; line-height: 1.5;'>
                    Obrigado por se cadastrar no ValiData. Para ativar sua conta e acessar o sistema, utilize o código de segurança abaixo ou clique no link de validação:
                </p>

                <div style='text-align: center; margin: 30px 0;'>
                    <div style='display: inline-block; background: #f1f5f9; border: 2px dashed #6366f1; border-radius: 10px; padding: 12px 28px; font-size: 2rem; font-weight: 800; letter-spacing: 6px; color: #4338ca;'>
                        {codigoConfirmacao}
                    </div>
                </div>

                <div style='text-align: center; margin-bottom: 30px;'>
                    <a href='{linkConfirmacao}' style='background: #4f46e5; color: #ffffff; text-decoration: none; padding: 12px 24px; border-radius: 8px; font-weight: bold; display: inline-block;'>
                        Validar Minha Conta Agora
                    </a>
                </div>

                <p style='color: #94a3b8; font-size: 0.8rem; text-align: center; margin-top: 30px; border-top: 1px solid #f1f5f9; padding-top: 16px;'>
                    Este código expira em 24 horas. Se você não solicitou este cadastro, ignore esta mensagem.
                </p>
            </div>
        ";

        // Registra simulação para desenvolvimento/testes locais
        _ultimoEmailSimulado = new UltimoEmailEnviado
        {
            Destinatario = email,
            Assunto = assunto,
            CodigoConfirmacao = codigoConfirmacao,
            LinkConfirmacao = linkConfirmacao,
            DataEnvio = DateTime.Now
        };

        _logger.LogInformation($"[E-MAIL SIMULADO] Para: {email} | Código: {codigoConfirmacao} | Link: {linkConfirmacao}");

        // Tentativa de envio real via SMTP se variáveis de ambiente estiverem configuradas
        var smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST");
        var smtpPortStr = Environment.GetEnvironmentVariable("SMTP_PORT");
        var smtpUser = Environment.GetEnvironmentVariable("SMTP_USER");
        var smtpPass = Environment.GetEnvironmentVariable("SMTP_PASS");

        if (!OperatingSystem.IsBrowser() && !string.IsNullOrWhiteSpace(smtpHost) && !string.IsNullOrWhiteSpace(smtpUser) && !string.IsNullOrWhiteSpace(smtpPass))
        {
            try
            {
                int smtpPort = int.TryParse(smtpPortStr, out var p) ? p : 587;
                using var client = new SmtpClient(smtpHost, smtpPort)
                {
                    Credentials = new NetworkCredential(smtpUser, smtpPass),
                    EnableSsl = true
                };

                var mail = new MailMessage
                {
                    From = new MailAddress(smtpUser, "ValiData"),
                    Subject = assunto,
                    Body = corpoHtml,
                    IsBodyHtml = true
                };
                mail.To.Add(email);

                await client.SendMailAsync(mail);
                _logger.LogInformation($"[E-MAIL ENVIADO COM SUCESSO VIA SMTP] Para: {email}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"[SMTP FALHOU - MANTENDO MODO SIMULADO] Erro ao enviar e-mail para {email}");
            }
        }

        return true;
    }

    public async Task<bool> EnviarEmailRecuperacaoSenhaAsync(string nome, string email, string linkRecuperacao, string codigo)
    {
        _logger.LogInformation($"[RECUPERAÇÃO DE SENHA] Para: {email} | Código: {codigo}");
        await Task.CompletedTask;
        return true;
    }
}
