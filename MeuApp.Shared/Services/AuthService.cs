using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MeuApp.Shared.Data;
using MeuApp.Shared.Models;

namespace MeuApp.Shared.Services;

public class AuthService : IAuthService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IEmailService _emailService;
    private readonly ILogger<AuthService> _logger;
    private Usuario? _usuarioLogado;
    private readonly string _sessionFilePath;

    public event Action? OnAuthStateChanged;

    public AuthService(
        IDbContextFactory<AppDbContext> dbFactory,
        IEmailService emailService,
        ILogger<AuthService> logger)
    {
        _dbFactory = dbFactory;
        _emailService = emailService;
        _logger = logger;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(localAppData, "ValiDataApp");
        Directory.CreateDirectory(appFolder);
        _sessionFilePath = Path.Combine(appFolder, "sessao.json");
    }

    public async Task<Usuario?> ObterUsuarioLogadoAsync()
    {
        if (_usuarioLogado != null)
            return _usuarioLogado;

        try
        {
            if (File.Exists(_sessionFilePath))
            {
                var json = await File.ReadAllTextAsync(_sessionFilePath);
                var sessao = JsonSerializer.Deserialize<SessaoPersistida>(json);
                if (sessao != null && sessao.UsuarioId > 0)
                {
                    await using var db = await _dbFactory.CreateDbContextAsync();
                    var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == sessao.UsuarioId && u.Ativo);
                    if (usuario != null)
                    {
                        _usuarioLogado = usuario;
                        return _usuarioLogado;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao recuperar sessão persistida do usuário.");
        }

        return null;
    }

    public async Task<ResultadoAuth> LoginAsync(string email, string senha)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(senha))
            return ResultadoAuth.Falha("Informe o e-mail e a senha.");

        email = email.Trim().ToLowerInvariant();

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == email);

            if (usuario == null)
                return ResultadoAuth.Falha("E-mail ou senha incorretos.");

            if (!VerificarHashSenha(senha, usuario.SenhaHash, usuario.SenhaSalt))
                return ResultadoAuth.Falha("E-mail ou senha incorretos.");

            if (!usuario.Ativo)
                return ResultadoAuth.Falha("Esta conta foi desativada.");

            if (!usuario.EmailConfirmado)
            {
                return ResultadoAuth.Falha(
                    "Seu e-mail ainda não foi confirmado. Por favor, valide o código ou link enviado para sua caixa de entrada.",
                    requerConfirmacao: true);
            }

            usuario.UltimoAcesso = DateTime.Now;
            await db.SaveChangesAsync();

            _usuarioLogado = usuario;
            await SalvarSessaoAsync(usuario.Id);
            OnAuthStateChanged?.Invoke();

            return ResultadoAuth.Ok(usuario, $"Bem-vindo de volta, {usuario.Nome}!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao realizar login.");
            return ResultadoAuth.Falha("Ocorreu um erro interno ao realizar login. Tente novamente.");
        }
    }

    public async Task<ResultadoAuth> CadastrarAsync(string nome, string email, string senha, string baseUrl = "")
    {
        if (string.IsNullOrWhiteSpace(nome))
            return ResultadoAuth.Falha("Informe seu nome completo.");

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return ResultadoAuth.Falha("Informe um endereço de e-mail válido.");

        if (string.IsNullOrWhiteSpace(senha) || senha.Length < 6)
            return ResultadoAuth.Falha("A senha deve conter no mínimo 6 caracteres.");

        email = email.Trim().ToLowerInvariant();
        nome = nome.Trim();

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var existente = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == email);
            if (existente != null)
            {
                if (existente.EmailConfirmado)
                {
                    return ResultadoAuth.Falha("Já existe uma conta cadastrada com este e-mail.");
                }
                else
                {
                    // Atualiza dados e reenvia token
                    existente.Nome = nome;
                    CriarHashSenha(senha, out var hash, out var salt);
                    existente.SenhaHash = hash;
                    existente.SenhaSalt = salt;
                    existente.TokenConfirmacao = Guid.NewGuid().ToString("N");
                    existente.CodigoConfirmacao = Random.Shared.Next(100000, 999999).ToString();
                    existente.TokenExpiracao = DateTime.Now.AddHours(24);

                    await db.SaveChangesAsync();

                    var linkExistente = $"{baseUrl.TrimEnd('/')}/confirmar-email?token={existente.TokenConfirmacao}";
                    await _emailService.EnviarEmailConfirmacaoAsync(nome, email, linkExistente, existente.CodigoConfirmacao);

                    var resReenvio = ResultadoAuth.Ok(existente, "Cadastro atualizado! Enviamos um novo link de confirmação para o seu e-mail.");
                    resReenvio.RequerConfirmacaoEmail = true;
                    resReenvio.CodigoGeradoSimulacao = existente.CodigoConfirmacao;
                    resReenvio.LinkGeradoSimulacao = linkExistente;
                    return resReenvio;
                }
            }

            CriarHashSenha(senha, out var novoHash, out var novoSalt);

            var token = Guid.NewGuid().ToString("N");
            var codigo = Random.Shared.Next(100000, 999999).ToString();

            var novoUsuario = new Usuario
            {
                Nome = nome,
                Email = email,
                SenhaHash = novoHash,
                SenhaSalt = novoSalt,
                EmailConfirmado = false,
                TokenConfirmacao = token,
                CodigoConfirmacao = codigo,
                TokenExpiracao = DateTime.Now.AddHours(24),
                DataCriacao = DateTime.Now,
                Ativo = true
            };

            db.Usuarios.Add(novoUsuario);
            await db.SaveChangesAsync();

            var link = $"{baseUrl.TrimEnd('/')}/confirmar-email?token={token}";
            await _emailService.EnviarEmailConfirmacaoAsync(nome, email, link, codigo);

            var resultado = ResultadoAuth.Ok(novoUsuario, "Cadastro realizado com sucesso! Verifique seu e-mail para validar a conta.");
            resultado.RequerConfirmacaoEmail = true;
            resultado.CodigoGeradoSimulacao = codigo;
            resultado.LinkGeradoSimulacao = link;
            return resultado;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao cadastrar usuário.");
            return ResultadoAuth.Falha("Erro ao cadastrar usuário. Tente novamente.");
        }
    }

    public async Task<ResultadoAuth> ConfirmarEmailAsync(string tokenOuCodigo)
    {
        if (string.IsNullOrWhiteSpace(tokenOuCodigo))
            return ResultadoAuth.Falha("Informe o código ou token de confirmação.");

        tokenOuCodigo = tokenOuCodigo.Trim();

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var usuario = await db.Usuarios.FirstOrDefaultAsync(u =>
                (u.CodigoConfirmacao == tokenOuCodigo || u.TokenConfirmacao == tokenOuCodigo) && !u.EmailConfirmado);

            if (usuario == null)
            {
                // Verifica se já estava confirmado
                var jaConfirmado = await db.Usuarios.AnyAsync(u =>
                    (u.CodigoConfirmacao == tokenOuCodigo || u.TokenConfirmacao == tokenOuCodigo) && u.EmailConfirmado);

                if (jaConfirmado)
                    return ResultadoAuth.Ok(new Usuario(), "Este e-mail já foi validado anteriormente! Faça login para continuar.");

                return ResultadoAuth.Falha("Código ou link de validação inválido ou expirado.");
            }

            if (usuario.TokenExpiracao.HasValue && usuario.TokenExpiracao.Value < DateTime.Now)
            {
                return ResultadoAuth.Falha("Este código ou link de validação já expirou. Solicite um novo reenvio.");
            }

            usuario.EmailConfirmado = true;
            usuario.TokenConfirmacao = null;
            usuario.CodigoConfirmacao = null;
            usuario.TokenExpiracao = null;

            await db.SaveChangesAsync();

            return ResultadoAuth.Ok(usuario, "E-mail validado com sucesso! Agora você já pode fazer login.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao confirmar e-mail.");
            return ResultadoAuth.Falha("Erro ao confirmar e-mail. Tente novamente.");
        }
    }

    public async Task<ResultadoAuth> ReenviarConfirmacaoAsync(string email, string baseUrl = "")
    {
        if (string.IsNullOrWhiteSpace(email))
            return ResultadoAuth.Falha("Informe o e-mail cadastrado.");

        email = email.Trim().ToLowerInvariant();

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == email);

            if (usuario == null)
                return ResultadoAuth.Falha("Nenhuma conta encontrada com este e-mail.");

            if (usuario.EmailConfirmado)
                return ResultadoAuth.Ok(usuario, "Este e-mail já está validado! Pode fazer login.");

            usuario.TokenConfirmacao = Guid.NewGuid().ToString("N");
            usuario.CodigoConfirmacao = Random.Shared.Next(100000, 999999).ToString();
            usuario.TokenExpiracao = DateTime.Now.AddHours(24);

            await db.SaveChangesAsync();

            var link = $"{baseUrl.TrimEnd('/')}/confirmar-email?token={usuario.TokenConfirmacao}";
            await _emailService.EnviarEmailConfirmacaoAsync(usuario.Nome, usuario.Email, link, usuario.CodigoConfirmacao);

            var res = ResultadoAuth.Ok(usuario, "Novo código de validação enviado para o seu e-mail!");
            res.CodigoGeradoSimulacao = usuario.CodigoConfirmacao;
            res.LinkGeradoSimulacao = link;
            return res;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao reenviar confirmação.");
            return ResultadoAuth.Falha("Erro ao reenviar código de validação.");
        }
    }

    public async Task<ResultadoAuth> AtualizarPerfilAsync(int usuarioId, string nome, string? senhaAtual, string? novaSenha)
    {
        if (string.IsNullOrWhiteSpace(nome))
            return ResultadoAuth.Falha("O nome não pode ficar em branco.");

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId);

            if (usuario == null)
                return ResultadoAuth.Falha("Usuário não encontrado.");

            usuario.Nome = nome.Trim();

            if (!string.IsNullOrWhiteSpace(novaSenha))
            {
                if (string.IsNullOrWhiteSpace(senhaAtual))
                    return ResultadoAuth.Falha("Informe a senha atual para alterá-la.");

                if (!VerificarHashSenha(senhaAtual, usuario.SenhaHash, usuario.SenhaSalt))
                    return ResultadoAuth.Falha("A senha atual informada está incorreta.");

                if (novaSenha.Length < 6)
                    return ResultadoAuth.Falha("A nova senha deve ter no mínimo 6 caracteres.");

                CriarHashSenha(novaSenha, out var novoHash, out var novoSalt);
                usuario.SenhaHash = novoHash;
                usuario.SenhaSalt = novoSalt;
            }

            await db.SaveChangesAsync();
            _usuarioLogado = usuario;
            OnAuthStateChanged?.Invoke();

            return ResultadoAuth.Ok(usuario, "Perfil atualizado com sucesso!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar perfil.");
            return ResultadoAuth.Falha("Erro ao atualizar dados do perfil.");
        }
    }

    public async Task LogoutAsync()
    {
        _usuarioLogado = null;
        try
        {
            if (File.Exists(_sessionFilePath))
                File.Delete(_sessionFilePath);
        }
        catch
        {
            // Tratamento defensivo
        }

        OnAuthStateChanged?.Invoke();
        await Task.CompletedTask;
    }

    private async Task SalvarSessaoAsync(int usuarioId)
    {
        try
        {
            var sessao = new SessaoPersistida { UsuarioId = usuarioId, DataLogin = DateTime.Now };
            var json = JsonSerializer.Serialize(sessao);
            await File.WriteAllTextAsync(_sessionFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível persistir a sessão em disco.");
        }
    }

    private static void CriarHashSenha(string senha, out string hash, out string salt)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        salt = Convert.ToBase64String(saltBytes);

        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(senha, saltBytes, 10000, HashAlgorithmName.SHA256, 32);
        hash = Convert.ToBase64String(hashBytes);
    }

    private static bool VerificarHashSenha(string senha, string hashArmazenado, string saltArmazenado)
    {
        try
        {
            var saltBytes = Convert.FromBase64String(saltArmazenado);
            var hashCalculadoBytes = Rfc2898DeriveBytes.Pbkdf2(senha, saltBytes, 10000, HashAlgorithmName.SHA256, 32);
            var hashCalculado = Convert.ToBase64String(hashCalculadoBytes);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(hashCalculado),
                Encoding.UTF8.GetBytes(hashArmazenado));
        }
        catch
        {
            return false;
        }
    }

    private class SessaoPersistida
    {
        public int UsuarioId { get; set; }
        public DateTime DataLogin { get; set; }
    }
}
