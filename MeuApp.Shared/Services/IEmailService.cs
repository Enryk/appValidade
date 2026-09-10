using System.Threading.Tasks;

namespace MeuApp.Shared.Services;

public interface IEmailService
{
    Task<bool> EnviarEmailConfirmacaoAsync(string nome, string email, string linkConfirmacao, string codigoConfirmacao);
    Task<bool> EnviarEmailRecuperacaoSenhaAsync(string nome, string email, string linkRecuperacao, string codigo);
    
    // Objeto para inspecionar em modo demonstração/desenvolvimento
    UltimoEmailEnviado? ObterUltimoEmailSimulado();
}

public class UltimoEmailEnviado
{
    public string Destinatario { get; set; } = string.Empty;
    public string Assunto { get; set; } = string.Empty;
    public string CodigoConfirmacao { get; set; } = string.Empty;
    public string LinkConfirmacao { get; set; } = string.Empty;
    public System.DateTime DataEnvio { get; set; } = System.DateTime.Now;
}
