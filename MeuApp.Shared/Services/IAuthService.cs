using System;
using System.Threading.Tasks;
using MeuApp.Shared.Models;

namespace MeuApp.Shared.Services;

public class ResultadoAuth
{
    public bool Sucesso { get; set; }
    public string Mensagem { get; set; } = string.Empty;
    public Usuario? Usuario { get; set; }
    public bool RequerConfirmacaoEmail { get; set; }
    public string? CodigoGeradoSimulacao { get; set; }
    public string? LinkGeradoSimulacao { get; set; }

    public static ResultadoAuth Ok(Usuario usuario, string mensagem = "Operação realizada com sucesso!") =>
        new() { Sucesso = true, Mensagem = mensagem, Usuario = usuario };

    public static ResultadoAuth Falha(string mensagem, bool requerConfirmacao = false) =>
        new() { Sucesso = false, Mensagem = mensagem, RequerConfirmacaoEmail = requerConfirmacao };
}

public interface IAuthService
{
    Task<ResultadoAuth> LoginAsync(string email, string senha);
    Task<ResultadoAuth> CadastrarAsync(string nome, string email, string senha, string baseUrl = "");
    Task<ResultadoAuth> ConfirmarEmailAsync(string tokenOuCodigo);
    Task<ResultadoAuth> ReenviarConfirmacaoAsync(string email, string baseUrl = "");
    Task LogoutAsync();
    Task<Usuario?> ObterUsuarioLogadoAsync();
    Task<ResultadoAuth> AtualizarPerfilAsync(int usuarioId, string nome, string? senhaAtual, string? novaSenha);
    
    event Action? OnAuthStateChanged;
}
