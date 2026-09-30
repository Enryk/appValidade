using System.Threading.Tasks;

namespace MeuApp.Shared.Services;

public interface IEmailService
{
    Task<bool> EnviarEmailAtivacaoAsync(string nome, string email, string linkAtivacao, string codigo);
    Task<bool> EnviarEmailConfirmacaoAsync(string nome, string email, string linkConfirmacao, string codigoConfirmacao);
    Task<bool> EnviarEmailRecuperacaoSenhaAsync(string nome, string email, string linkRecuperacao, string codigo);
}
